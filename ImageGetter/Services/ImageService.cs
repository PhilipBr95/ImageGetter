using ImageGetter.Controllers;
using ImageGetter.Models;
using Microsoft.AspNetCore.Mvc.Routing;
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

        public async Task<ImageWithMeta?> RetrieveImageAsync(int mediaId, int? width = null, int? height = null, bool debug = false)
        {
            return await RetrieveImageAsync(filename: null, width, height, debug, mediaId);
        }

        public async Task<ImageWithMeta?> RetrieveImageAsync(string filename, int? width = null, int? height = null, bool debug = false)
        {
            return await RetrieveImageAsync(filename, width, height, debug, null);
        }
        
        public async Task<ImageWithMeta?> RetrieveImageAsync(string? filename = null, int? width = null, int? height = null, bool debug = false, int? mediaId = null)
        {
            Media media = null;

            try
            {
                if (mediaId.HasValue)
                {
                    media = _imageService.GetImage(mediaId.Value);
                    filename = media.Filename;

                    _logger.LogInformation($"{media.Filename} -> {HttpUtility.UrlEncode(media.Filename)}");
                }
                else if (string.IsNullOrWhiteSpace(filename))
                {
                    media = _imageService.GetRandomImage();
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
                var caption = $"{file.MediaId} - {file.ParentFolderName}{(file.CreatedDate.Year > 1900 ? $" @ {createdDate}" : "")}\n{location}";

                if (debug)
                    caption += $"\n{filename}";

                AddText(caption, image, 0, landscape, debug);

                return new ImageWithMeta(image, filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to retrieve image: {filename} - mediaId: {media?.MediaId}");
                return null;
            }
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
            try
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
                    var bestFaces = faces?.Where(w => w.ConfidenceMultiplyer > _settings.MinConfidenceMultiplier)
                                          .OrderByDescending(o => o.ConfidenceMultiplyer);

                    //Default x, y of the crop.
                    int x = 0;
                    int y = 0;

                    //Default crop
                    var cropRect = GetCropRectangle(centerCoordinates, width.Value, height.Value, image, targetRatio);

                    if (debug)
                        image.Mutate(ctx => ctx.Draw(Color.Brown, 6f, new RectangularPolygon(cropRect)));

                    //Did we find a face with reasonable confidence?
                    if (bestFaces?.Any() == false)
                        bestFaces = faces?.Where(w => w.Confidence > _settings.MinConfidence)
                                          .OrderByDescending(o => o.ConfidenceMultiplyer);

                    if (bestFaces?.Any() == true)
                    {
                        centerCoordinates = new Point(x / bestFaces.Count(), y / bestFaces.Count());
                        cropRect = GetCropRectangle(centerCoordinates, width.Value, height.Value, image, targetRatio);
                        var moveAllowed = true;

                        //Did we lose anybody?
                        foreach (var face in bestFaces)
                        {
                            if (moveAllowed)
                            {
                                if (face.Y + face.Height > cropRect.Y + cropRect.Height)
                                {
                                    moveAllowed = false;

                                    _logger.LogDebug($"Face at {face.X},{face.Y} lost at bottom after crop");
                                    var heightDiff = (face.Y + face.Height) - (cropRect.Y + cropRect.Height) + (face.Width / 2);
                                    //height += heightDiff;

                                    cropRect = new Rectangle(cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height + heightDiff);
                                }
                                else if (face.Y < cropRect.Y)
                                {
                                    moveAllowed = false;

                                    _logger.LogDebug($"Face at {face.X},{face.Y} lost at top after crop");
                                    var heightDiff = cropRect.Y - face.Y + (face.Height / 2);
                                    //height += heightDiff;

                                    centerCoordinates = new Point(centerCoordinates.X, centerCoordinates.Y - heightDiff);
                                    cropRect = new Rectangle(cropRect.X, cropRect.Y - heightDiff, cropRect.Width, cropRect.Height);
                                }
                            }
                        }

                        //var topFace = bestFaces.OrderBy(o => o.Y)
                        //                       .First();
                        //var topFaceY = topFace.Y - topFace.Height / 2;
                        //if (topFaceY < 0)
                        //    topFaceY = 0;

                        //centerCoordinates = new Point(centerCoordinates.X, centerCoordinates.Y - topFaceY);
                        //cropRect = new Rectangle(cropRect.X, topFaceY, cropRect.Width, cropRect.Height);

                    }
                    else
                        _logger.LogDebug($"None of the faces look good :-(... Max Confidence: {faces?.Max(m => m.Confidence)}");

                    _logger.LogDebug($"Resizing image: {image.Width}x{image.Height} with Center {centerCoordinates} to {cropRect.Width}x{cropRect.Height}");

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
            catch (Exception e)
            {
                _logger.LogError(e, $"Oops - MediaId:{file.MediaId} - {HttpUtility.UrlEncode(file.Filename)}");
                throw;
            }
        }

        private Rectangle GetCropRectangle(Point centerCoordinates, int desiredWidth, int desiredHeight, Image image, float requiredImageRatio)
        {
            int x = 0;
            int y = 0;
            int width = 0;
            int height = 0;

            //Which is worse, Height or Width?
            var xDiff = desiredWidth / (double)image.Width;
            var yDiff = desiredHeight / (double)image.Height;
            bool imageTooBig = true;
            bool widthBest = true;

            if (xDiff < 1 && yDiff < 1)
                imageTooBig = false;

            //Is height the issue?
            if (imageTooBig && xDiff > yDiff)
            {                
                x = 0;
                width = image.Width;
                height = (int)(width / requiredImageRatio);
                y = (image.Height - height) / 2;
            }
            else if (imageTooBig)
            {
                y = 0;
                height = image.Height;
                width = (int)(height * requiredImageRatio);
                x = (image.Width - width) / 2;
            }
            else
            {
                //if(xDiff > yDiff)
                //{
                //    //Use desiredHeight

                //    width = desiredWidth;
                //}
                //else
                //{
                //    //Use desiredWidth
                //    height = desiredHeight;
                //}

                //x = centerCoordinates.X - desiredWidth / 2;
                //y = centerCoordinates.Y - desiredHeight / 2;

                //if (x + width > image.Width)
                //{
                //    var xExcess = (x + width) - image.Width;
                //    x -= xExcess;

                //    if (x < 0)
                //        x = 0;

                //    var yChange = (int)(xExcess / requiredImageRatio);
                //    y -= yChange;
                //}
                //else
                //{
                //    var xExpand = x;
                //    var yExpand = (int)(xExpand / requiredImageRatio);

                //    x -= xExpand;
                //    y -= yExpand;

                //    width += xExpand * 2;
                //    height += yExpand * 2;
                //}

                //Image too small
                x = 0;
                y = 0;
                width = image.Width;
                height = image.Height;


                //if(xDiff > yDiff)
                //{
                //    //Use X
                //    width = image.Width;
                //    height = (int)(width / requiredImageRatio);
                //    x = centerCoordinates.X - ((image.Width - width) / 2);
                //    y = centerCoordinates.Y - ((image.Height - height) / 2);
                //}
                //else
                //{
                //    //Use Y
                //    height = desiredHeight;
                //    width = (int)(height * requiredImageRatio);
                //    x = centerCoordinates.X - ((image.Width - width) / 2);
                //    y = centerCoordinates.Y - ((image.Height - height) / 2);
                //}

                //if (x + width > image.Width)
                //{
                //    var xExcess = (x + width) - image.Width;
                //    x -= xExcess;

                //    if (x < 0)
                //        x = 0;

                //    var yChange = (int)(xExcess / requiredImageRatio);
                //    y -= yChange;
                //}
                //else
                //{
                //    var xExpand = x;
                //    var yExpand = (int)(xExpand / requiredImageRatio);

                //    x -= xExpand;
                //    y -= yExpand;

                //    width += xExpand * 2;
                //    height += yExpand * 2;
                //}


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
                _logger.LogWarning($"Unexpected FindFace response: {faceString}");
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
