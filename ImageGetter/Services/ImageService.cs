using ImageGetter.Controllers;
using ImageGetter.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Web;

namespace ImageGetter.Services
{
    public class ImageService : IImageService
    {
        private readonly IImageRetrievalService _imageService;
        private readonly ILogger<ImageController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Settings _settings;
        private IMemoryCache _memoryCache;

        public ImageService(IImageRetrievalService imageService, IHttpClientFactory httpClientFactory, IOptions<Settings> settings, ILogger<ImageController> logger, IMemoryCache memoryCache)
        {
            _imageService = imageService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _memoryCache = memoryCache;
        }

        private static bool _cachingInProgress = false;

        public async Task CacheImageAsync(int? width = null, int? height = null)
        {
            if (_cachingInProgress)
            {
                _logger.LogInformation("Caching already in progress, skipping...");
                return;
            }

            _cachingInProgress = true;

            try
            {
                var newImage = await RetrieveImageAsync(null, width, height);
                _memoryCache.Set<ImageWithMeta>("CachedRandomImage", newImage, TimeSpan.FromDays(1));

                _logger.LogInformation($"Cached an image: {newImage.Filename}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache image");
            }

            _cachingInProgress = false;
        }

        public async Task<ImageWithMeta?> GetCachedImageAsync(int? width = null, int? height = null)
        {
            if (_memoryCache.TryGetValue("CachedRandomImage", out ImageWithMeta imageWithMeta) == true)
            {
                //Kick off a background cache refresh for the next request
                _ = Task.Run(() => CacheImageAsync(width, height));

                return imageWithMeta;
            }

            _logger.LogWarning("Cache miss :-(");

            //Cache miss - get a new image
            return await RetrieveImageAsync();
        }

        public async Task<ImageWithMeta?> RetrieveImageAsync(string? filename = null, int? width = null, int? height = null, bool debug = false)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                var media = _imageService.GetRandomImage();
                filename = media.Filename;

                _logger.LogInformation($"{media.Filename} -> {HttpUtility.UrlEncode(media.Filename)}");
            }
            else
                filename = HttpUtility.UrlDecode(filename);

            var file = _imageService.GetImage(filename);
            if (file == null)
            {
                _logger.LogError($"Failed to find image {filename}");
                return null;
            }

            var image = await Image.LoadAsync(new MemoryStream(file.Data));
            image.Mutate(x => x.AutoOrient());
            image = await ResizeImageAsync(width, height, debug, file, image);

            var landscape = file.IsLandscape;
            _logger.LogDebug($"{(landscape ? "Landscape mode" : "Portrate mode")} - Orientation:{file.Orientation} - Dimensions:{image.Width}x{image.Height}");

            var createdDate = file.CreatedDate.ToString("dd/MMM/yyyy");
            var location = file.Location;
            var caption = $"{file.ParentFolderName}{(file.CreatedDate.Year > 1900 ? $" @ {createdDate}" : "")}\n{location}";

            if (debug)
                caption += $"\n{filename}";

            AddText(caption, image, 0, landscape, debug);

            return new ImageWithMeta(image, filename);
        }

        private async Task<Image> ResizeImageAsync(int? width, int? height, bool debug, MediaFile file, Image image)
        {
            if (width == null && height == null)
                return image;

            _logger.LogDebug($"Resizing image to {width}x{height} from {image.Width}x{image.Height}");

            //Default to center crop
            PointF centerCoordinates = new PointF(image.Width / 2, image.Height / 2);

            var ignoreX = false;
            var ignoreY = false;

            if (width > image.Width)
            {
                ignoreX = true;
                width = image.Width;
            }

            if (height > image.Height)
            {
                ignoreY = true;
                height = image.Height;
            }

            //Resolution: 1280 x 800 pixels => 3200 x 2000 => 3200/2000
            var imageRatio = (float)image.Width / image.Height;
            var targetRatio = (float)(width ?? image.Width) / (height ?? image.Height);
            var ratioDiff = Math.Abs(imageRatio - targetRatio);
            var useImageCenter = ratioDiff > _settings.ImageRatioTolerance;

            _logger.LogInformation($"Image ratio is {imageRatio}, Target ratio is {targetRatio}. Diff: {ratioDiff}");

            if (useImageCenter)
            {
                IEnumerable<Face>? faces = await FindFaces(file);
                var bestFaces = faces?.Where(w => w.Confidence > _settings.MinConfidence)
                                        .OrderByDescending(o => o.Confidence);

                var x = (image.Width / 2) - (width.Value / 2);
                var y = (image.Height / 2) - (height.Value / 2);

                if (faces == null || faces.Count() == 0)
                {
                    //Can we do some clever resizing
                    if (image.Width > width && image.Height > height)
                    {
                        x = 0;
                        width = image.Width;

                        var newHeight = (int)(image.Width / targetRatio);
                        y -= (newHeight - (height ?? image.Height)) / 2;
                        height = newHeight;
                    }
                }

                //Default crop
                var cropRect = new Rectangle(x, y, width.Value, height.Value);

                //Did we find a face with reasonable confidence?
                if (bestFaces?.Any() == true)
                {
                    //Check for good faces
                    var avgFaces = bestFaces.Where(w => w.Confidence > _settings.MinAvgConfidence && w.Height > _settings.MinAvgHeight);

                    //Average out the best faces
                    if (avgFaces.Any())
                    {
                        x = 0; y = 0;

                        foreach (var face in avgFaces)
                        {
                            _logger.LogDebug($"Averaging Face found at {face.X},{face.Y} size {face.Width}x{face.Height} with {face.Confidence} Confidence");
                            x += face.X + (face.Width / 2);
                            y += face.Y + (face.Height / 2);
                        }

                        centerCoordinates = new PointF(x / avgFaces.Count(), y / avgFaces.Count());
                    }
                    else
                    {
                        //Just focus on the best
                        var face = bestFaces.Where(w => w.Height > _settings.MinHeight)
                                            .OrderByDescending(o => o.Confidence).FirstOrDefault();

                        if (face != null)
                        {
                            _logger.LogDebug($"Face found at {face.X},{face.Y} size {face.Width}x{face.Height} with {face.Confidence} Confidence");
                            centerCoordinates = new PointF(face.X + (face.Width / 2), face.Y + (face.Height / 2));
                        }
                        else
                            _logger.LogDebug($"No faces are tall enough. Min Face Height: {bestFaces.Min(m => m.Height)}. MinHeight: {_settings.MinHeight}");
                    }

                    x = (int)centerCoordinates.X - (width.Value / 2);
                    if (ignoreX || x < 0)
                        x = 0;

                    y = (int)centerCoordinates.Y - (height.Value! / 2);
                    if (ignoreY || y < 0)
                        y = 0;

                    if (x + width.Value > image.Width)
                        x = image.Width - width.Value;

                    if (y + height.Value > image.Height)
                        y = image.Height - height.Value;

                    cropRect = new Rectangle((int)x, (int)y, width.Value, height.Value);
                }
                else
                    _logger.LogDebug($"None of the faces look good :-(... Max Confidence: {faces?.Max(m => m.Confidence)}");

                _logger.LogDebug($"Resizing image: {image.Width}x{image.Height} with Center {centerCoordinates}");

                if (debug == true)
                {
                    var cutRect = new RectangularPolygon(cropRect);
                    image.Mutate(ctx => ctx.Draw(Color.Orange, 6f, cutRect));

                    if (faces != null)
                    {
                        foreach (var face in faces)
                        {
                            var faceRect = new RectangularPolygon(face.X, face.Y, face.Width, face.Height);
                            image.Mutate(ctx => ctx.Draw(Color.Yellow, 6f, faceRect));
                        }
                    }
                    else                   
                    {
                        var centerRect = new RectangularPolygon(centerCoordinates.X - 15, centerCoordinates.Y - 15, 30, 30);
                        image.Mutate(ctx => ctx.Fill(Color.OrangeRed, centerRect));
                    }
                }
                else
                    image.Mutate(x => x.Crop(cropRect));
            }
            else
                _logger.LogInformation($"Not Resizing as the image ratio is {ratioDiff}");

            return image;
        }

        private async Task<IEnumerable<Face>?> FindFaces(MediaFile file)
        {
            var http = _httpClientFactory.CreateClient();
            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(new MemoryStream(file.Data));

            content.Add(fileContent, "file", "fileName");
            var faceResponse = await http.PostAsync(_settings.FaceApi, content);

            string faceString = "";
            Face[]? faces = null;

            try
            {
                faceString = await faceResponse.Content.ReadAsStringAsync();
                faces = JsonSerializer.Deserialize<Face[]>(faceString);

                _logger.LogInformation($"Discovered {faces.Length} faces");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to deserialize FindFace response: {faceString}");
                return null;
            }

            if (faces.Length == 0 || faces[0]?.X == 0)
            {
                _logger.LogError($"Unexpected FindFace response: {faceString}");
                return null;
            }

            return faces;
        }

        private void AddText(string text, Image image, int yOffset, bool landscape, bool debug)
        {
            const float TEXTPADDING = 18f;

            float textFontSize = 150f;

            FontCollection fontCollection = new();
            fontCollection.Add("Fonts/OpenSans-VariableFont_wdth,wght.ttf");

            if (!fontCollection.TryGet("Open Sans", out FontFamily fontFamily))
                throw new Exception($"Couldn't find the font");

            var sizing = true;
            FontRectangle fontRectangle = new();
            Font font = fontFamily.CreateFont(textFontSize, FontStyle.Regular);

            while (sizing)
            {
                font = fontFamily.CreateFont(textFontSize, FontStyle.Regular);

                var options = new TextOptions(font)
                {
                    Dpi = 72,
                    KerningMode = KerningMode.Auto,
                    TextDirection = TextDirection.LeftToRight
                };
                
                fontRectangle = TextMeasurer.MeasureSize(text, options);
                if (fontRectangle.Width < (image.Width - 50))
                    sizing = false;

                textFontSize -= 5;
            }

            var location = new PointF(image.Width - fontRectangle.Width - TEXTPADDING, image.Height - fontRectangle.Height - TEXTPADDING);

            location = new PointF(30, 30 + yOffset);
            var locationBack = new PointF(40, 40 + yOffset);
            var textRect = new RectangleF(location.X, location.Y, Math.Min(fontRectangle.Width, image.Width) - location.X, fontRectangle.Height - location.Y);

            if (debug)
                image.Mutate(ctx => ctx.Draw(Color.Coral, 6f, textRect));

            //Figure out the avg background colour
            var croppedImageResizedToOnePixel = image.Clone(
                img => img.Crop((Rectangle)textRect)
                          .Resize(new Size(1, 1))
             );

            var averageColor = croppedImageResizedToOnePixel.CloneAs<Rgba32>()[0, 0];
            var luminance = (0.299 * averageColor.R + 0.587 * averageColor.G + 0.114 * averageColor.B) / 255;

            var mainColour = luminance > 0.5 ? Color.Black : Color.White;
            var outerColour = luminance > 0.5 ? Color.White : Color.Black;

            _logger.LogInformation($"Found luminance: {luminance} for text: {text}");

            image.Mutate(x => x.DrawText(text, font, outerColour, locationBack)
                               .DrawText(text, font, mainColour, location));
        }

    }
}
