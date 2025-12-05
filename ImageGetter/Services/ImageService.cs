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

        public async Task CacheImageAsync(int? width = null, int? height = null, bool debug = false)
        {
            if (_cachingInProgress)
            {
                _logger.LogInformation("Caching already in progress, skipping...");
                return;
            }

            _cachingInProgress = true;

            try
            {
                var newImage = await RetrieveImageAsync(null, width, height, debug);
                _memoryCache.Set<ImageWithMeta>("CachedRandomImage", newImage, TimeSpan.FromDays(1));

                _logger.LogInformation($"Cached an image: {newImage.Filename}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache image");
            }

            _cachingInProgress = false;
        }

        public async Task<ImageWithMeta?> GetCachedImageAsync(int? width = null, int? height = null, bool debug = false)
        {
            if (_memoryCache.TryGetValue("CachedRandomImage", out ImageWithMeta imageWithMeta) == true)
            {
                //Kick off a background cache refresh for the next request
                _ = Task.Run(() => CacheImageAsync(width, height, debug));

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

            var dd = FormatLocation("Hello world, other world, my world, their world");
            var createdDate = file.CreatedDate.ToString("dd/MMM/yyyy");
            var location = FormatLocation(file.Location);
            var caption = $"{file.ParentFolderName}{(file.CreatedDate.Year > 1900 ? $" @ {createdDate}" : "")}\n{location}";

            if (debug)
                caption += $"\n{filename}";

            AddText(caption, image, 0, landscape, debug);

            return new ImageWithMeta(image, filename);
        }

        private static string FormatLocation(string location)
        {
            if (location.Length < 40)
                return location;

            var parts = location.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .ToArray();
            
            var maxLength = location.Length / 2;
            var formattedLocation = "";
            
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if ((formattedLocation + parts[i]).Length > maxLength)
                {
                    formattedLocation = string.Join(", ", parts.Where((a, b) => b <= i)) + ",\n" + formattedLocation;
                    break;
                }
                if (formattedLocation.Length == 0)
                    formattedLocation = parts[i];
                else
                    formattedLocation = parts[i] + ", " + formattedLocation;
            }

            return formattedLocation;
        }

        private async Task<Image> ResizeImageAsync(int? width, int? height, bool debug, MediaFile file, Image image)
        {
            if (width == null && height == null)
                return image;

            _logger.LogDebug($"Resizing image to {width}x{height} from {image.Width}x{image.Height}");

            //Default to center crop
            Point centerCoordinates = new Point(image.Width / 2, image.Height / 2);

            var ignoreX = false;
            var ignoreY = false;

            if (width < image.Width)
            {
                ignoreX = true;
                //width = image.Width;
            }

            if (height < image.Height)
            {
                ignoreY = true;
                //height = image.Height;
            }

            //Resolution: 1280 x 800 pixels => 3200 x 2000 => 3200/2000
            var imageRatio = (float)image.Width / image.Height;
            var targetRatio = (float)(width ?? image.Width) / (height ?? image.Height);
            var ratioDiff = Math.Abs(imageRatio - targetRatio);
            var useImageCenter = ratioDiff > _settings.ImageRatioTolerance;

            _logger.LogInformation($"Image ratio is {imageRatio}, Target ratio is {targetRatio}. Diff: {ratioDiff}");

            if (useImageCenter)
            {
                IEnumerable<Face>? faces = (await FindFacesAsync(file))?.Where(w => w.Confidence > 0);
                var bestFaces = faces?.Where(w => w.ConfidenceMultiplyer > _settings.MinConfidence)
                                        .OrderByDescending(o => o.ConfidenceMultiplyer);

                //Default x, y of the crop.
                int x = 0;
                int y = 0;

                //Default crop
                var cropRect = GetCropRectangle(centerCoordinates, width.Value, height.Value, image, targetRatio);

                if (debug)
                    image.Mutate(ctx => ctx.Draw(Color.Brown, 6f, new RectangularPolygon(cropRect)));
                
                //Did we find a face with reasonable confidence?
                if (bestFaces?.Any() == true)
                {
                    //Check for good faces
                    var avgFaceMultiplyer = bestFaces.Where(w => w.ConfidenceMultiplyer > _settings.MinAvgConfidence)
                                                     .Select(s => s.ConfidenceMultiplyer)
                                                     .DefaultIfEmpty(double.MaxValue)
                                                     .Average();
                    var avgFaces = bestFaces.Where(w => w.ConfidenceMultiplyer > avgFaceMultiplyer);

                    //Average out the best faces
                    if (avgFaces.Any())
                    {
                        x = 0; y = 0;

                        foreach (var face in avgFaces)
                        {
                            _logger.LogDebug($"Averaging Face found with {face.Confidence} Confidence, {face.ConfidenceMultiplyer}");
                            x += face.X + (face.Width / 2);
                            y += face.Y + (face.Height / 2);
                        }

                        centerCoordinates = new Point(x / avgFaces.Count(), y / avgFaces.Count());
                        cropRect = GetCropRectangle(centerCoordinates, width.Value, height.Value, image, targetRatio);

                        if (debug)
                            image.Mutate(ctx => ctx.Draw(Color.Brown, 6f, new RectangularPolygon(centerCoordinates, new SizeF(3, 3))));
                    }
                    else
                    {
                        //Just focus on the best
                        var face = bestFaces.Where(w => w.Height > _settings.MinHeight)
                                            .OrderByDescending(o => o.Confidence).FirstOrDefault();

                        if (face != null)
                        {
                            _logger.LogDebug($"Face found at {face.X},{face.Y} size {face.Width}x{face.Height} with {face.Confidence} Confidence");
                            centerCoordinates = new Point(face.X + (face.Width / 2), face.Y + (face.Height / 2));
                            cropRect = GetCropRectangle(centerCoordinates, width.Value, height.Value, image, targetRatio);
                        }
                        else
                            _logger.LogDebug($"No faces are tall enough. Min Face Height: {bestFaces.Min(m => m.Height)}. MinHeight: {_settings.MinHeight}");
                    }

                    
                    //NewMethod(ref width, ref height, image, centerCoordinates, targetRatio, out x, out y);

                    //x = (int)centerCoordinates.X - (width.Value / 2);
                    //if (ignoreX || x < 0)
                    //    x = 0;

                    //y = (int)centerCoordinates.Y - (height.Value! / 2);
                    //if (ignoreY || y < 0)
                    //    y = 0;

                    //if (x + width.Value > image.Width)
                    //    x = image.Width - width.Value;

                    //if (y + height.Value > image.Height)
                    //    y = image.Height - height.Value;

                    //if ((x > 0 || y > 0) && (width.Value < image.Width || height.Value < image.Height))
                    //{
                    //    int newX = x;
                    //    int newY = y;

                    //    //Expand it...
                    //    if (x > 0)
                    //    {
                    //        newX = 0;
                    //        newY = y - (x / (int)targetRatio);

                    //        if (newY < 0 || newY > image.Height)
                    //        {
                    //            //Not good
                    //            newX = x - (y * (int)targetRatio);
                    //            newY = 0;

                    //            if (newX < 0 || (x + newX > image.Width))
                    //            {
                    //                //Not good - Abort
                    //                newX = x;
                    //                newY = y;
                    //            }
                    //        }
                    //    }

                    //    if (newX != x || newY != y)
                    //    {
                    //        if (debug)
                    //        {
                    //            cropRect = new Rectangle((int)x, (int)y, width.Value, height.Value);

                    //            var cutRect = new RectangularPolygon(cropRect);
                    //            image.Mutate(ctx => ctx.Draw(Color.Blue, 6f, cutRect));
                    //        }

                    //        var diffX = x - newX;
                    //        var diffY = y - newY;

                    //        x = newX;
                    //        y = newY;

                    //        //x -= (int)(diffX / 2);
                    //        //y -= (int)(diffY / 2);

                    //        width = (int)(width.Value + (diffX * 2));
                    //        height = (int)(height.Value + (diffY * 2));
                    //    }

                    //    cropRect = new Rectangle((int)x, (int)y, width.Value, height.Value);
                    //}
                    //else
                    //    cropRect = new Rectangle((int)x, (int)y, width.Value, height.Value);
                }
                else
                    _logger.LogDebug($"None of the faces look good :-(... Max Confidence: {faces?.Max(m => m.Confidence)}");

                _logger.LogDebug($"Resizing image: {image.Width}x{image.Height} with Center {centerCoordinates}");

                if (debug)
                {                    
                    var cutRect = new RectangularPolygon(cropRect);
                    image.Mutate(ctx => ctx.Draw(Color.Orange, 6f, cutRect));

                    if (faces != null)
                    {
                        foreach (var face in faces)
                        {
                            (Font font, FontRectangle fontRectangle) = GetFont("random", image.Width, face.Width / 2);
                            var faceRect = new RectangularPolygon(face.X, face.Y, face.Width, face.Height);

                            image.Mutate(ctx => ctx.Draw(Color.Yellow, 6f, faceRect)
                                                   .DrawText($"{face.Confidence:0.0}", font, Color.Red, new PointF(face.X, face.Y)));
                        }
                    }

                    //Display the centre
                    var centerRect = new RectangularPolygon(centerCoordinates.X - 15, centerCoordinates.Y - 15, 30, 30);
                    image.Mutate(ctx => ctx.Fill(Color.OrangeRed, centerRect));
                }
                else
                    image.Mutate(x => x.Crop(cropRect));
            }
            else
                _logger.LogInformation($"Not Resizing as the image ratio is {ratioDiff}");

            return image;
        }

        private Rectangle GetCropRectangle(Point centerCoordinates, int desiredWidth, int desiredHeight, Image image, float requiredImageRatio)
        {
            int x = 0;
            int y = 0;
            int width = 0;
            int height = 0;

            if(desiredWidth > image.Width)
            {
                //var xDiff = desiredWidth - image.Width;
                //var xRatio = image.Width / (float)desiredWidth;

                x = 0;
                width = image.Width;
                height = (int)(width / requiredImageRatio);
                y = centerCoordinates.Y;
                //var yDiff = centerCoordinates.Y - y;

                y -= height / 2;
            }
            else if(desiredHeight > image.Height)
            {
                y = 0;
                height = image.Height;
                width = (int)(height * requiredImageRatio);
                x = centerCoordinates.X;
                //var xDiff = centerCoordinates.X - x;

                x -= width / 2;
            }
            else
            {
                width = desiredWidth;
                height = desiredHeight;
                x = centerCoordinates.X - desiredWidth / 2;
                y = centerCoordinates.Y - desiredHeight / 2;

                if(x + width > image.Width)
                {
                    var xExcess = (x + width) - image.Width;
                    x -= xExcess;

                    if (x < 0)
                        x = 0;

                    var yChange = (int)(xExcess / requiredImageRatio);
                    y -= yChange;
                }
                else
                {                    
                    var xExpand = x;
                    var yExpand = (int)(xExpand / requiredImageRatio);

                    x -= xExpand;
                    y -= yExpand;

                    width += xExpand * 2;
                    height += yExpand * 2;
                }
            }

            return new Rectangle(x, y, width, height);
        }

        private async Task<IEnumerable<Face>?> FindFacesAsync(MediaFile file)
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

        private static (Font, FontRectangle) GetFont(string text, int imageWidth, float textFontSize = 150f)
        {
            const float TEXTPADDING = 18f;

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
                if (fontRectangle.Width < (imageWidth - 50))
                    sizing = false;

                textFontSize -= 5;
            }

            return (font, fontRectangle);
        }

        private void AddText(string text, Image image, int yOffset, bool landscape, bool debug)
        {
            const float TEXTPADDING = 18f;

            (Font font, FontRectangle fontRectangle) = GetFont(text, image.Width);

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
