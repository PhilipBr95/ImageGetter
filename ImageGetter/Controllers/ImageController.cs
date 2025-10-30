using ImageGetter.Models;
using ImageGetter.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime;
using System.Text.Json;
using System.Web;

namespace ImageGetter.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly IImageService _imageService;
        private readonly ILogger<ImageController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Settings _settings;

        public ImageController(IImageService imageService, IHttpClientFactory httpClientFactory, IOptions<Settings> settings, ILogger<ImageController> logger) 
        {
            _imageService = imageService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
        }

        [HttpHead]
        public IActionResult Head()
        {
            Response.Headers.LastModified = DateTimeOffset.Now.ToString("ddd, d MMM yyyy HH:mm:ss");
            return Ok();
        }

        [HttpHead("/image/{width:int}/{height:int}")]
        public IActionResult Head(int width, int height)
        {
            return Head();
        }

        [HttpGet("/image")]
        public async Task<IActionResult> GetImage()
        {
            return await GetImage(null, null, null);
        }

        [HttpGet("/image/{filename:alpha?}")]
        public async Task<IActionResult> GetImage(string filename)
        {
            return await GetImage(filename, null, null);
        }

        [HttpGet("/image/{width:int}/{height:int}")]
        public async Task<IActionResult> GetImage(int width, int height)
        {
            return await GetImage(null, width, height);
        }


        [HttpGet("/image/{filename:alpha?}/{width:int?}/{height:int?}")]
        public async Task<IActionResult> GetImage(string? filename, int? width, int? height)
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
                return NotFound(filename);
            }

            var image = await Image.LoadAsync(new MemoryStream(file.Data));
            image.Mutate(x => x.AutoOrient());
            
            if (width != null || height != null)
            {
                _logger.LogDebug($"Resizing image to {width}x{height} from {image.Width}x{image.Height}");

                var resizeOptions = new ResizeOptions
                {
                    Size = new Size(width ?? image.Width, height ?? image.Height),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Lanczos3
                };

                Face? face = await FindFace(file);

                //Did we find a face with reasonable confidence?
                if (face?.Confidence > 5)
                {
                    _logger.LogDebug($"Face found at {face.X},{face.Y} size {face.Width}x{face.Height} with {face.Confidence} Confidence");
                    resizeOptions.CenterCoordinates = new PointF(face.X + (face.Width / 2), face.Y + (face.Height / 2));
                }

                _logger.LogDebug($"Resizing image: {image.Width}x{image.Height} with Center {resizeOptions.CenterCoordinates}");

                var faceRect = new RectangularPolygon(face.X, face.Y, face.Width, face.Height);
                image.Mutate(ctx => ctx.Draw(Color.Yellow, 6f, faceRect));

                //image.Mutate(x => x.Resize(resizeOptions));
            }

            var landscape = file.IsLandscape;
            _logger.LogDebug($"{(landscape ? "Landscape mode" : "Portrate mode")} - Orientation:{file.Orientation} - Dimensions:{image.Width}x{image.Height}");

            AddText(file.ParentFolderName, image, 0, landscape);

            var createdDate = file.CreatedDate.ToString("dd/MMM/yyyy");
            AddText(createdDate, image, 120, landscape);

            var location = file.Location;
            AddText(location, image, 240, landscape);

            MemoryStream ms = new();
            image.Save(ms, new JpegEncoder());
            return File(ms.ToArray(), "image/jpeg");
        }

        private async Task<Face?> FindFace(MediaFile file)
        {
            var http = _httpClientFactory.CreateClient();
            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(new MemoryStream(file.Data));

            content.Add(fileContent, "file", "fileName");
            var faceResponse = await http.PostAsync(_settings.FaceApi, content);

            string faceString = "";
            Face? face = null;

            try
            {
                faceString = await faceResponse.Content.ReadAsStringAsync();
                face = JsonSerializer.Deserialize<Face>(faceString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to deserialize FindFace response: {faceString}");
                return null;
            }

            if (face?.X == 0)
            {
                _logger.LogError($"Unexpected FindFace response: {faceString}");
                return null;
            }

            return face;
        }

        private void AddText(string text, Image image, int yOffset, bool landscape)
        {
            const float TEXTPADDING = 18f;
            
            float textFontSize = 100f;

            if (landscape)
            {
                textFontSize += 50f;
                yOffset += 30;
            }

            FontCollection fontCollection = new();            
            fontCollection.Add("Fonts/Roboto-Regular.ttf");
            
            if (!fontCollection.TryGet("Roboto", out FontFamily fontFamily))
                throw new Exception($"Couldn't find the font");

            var font = fontFamily.CreateFont(textFontSize, FontStyle.Regular);

            var options = new TextOptions(font)
            {
                Dpi = 72,
                KerningMode = KerningMode.Auto, TextDirection = TextDirection.LeftToRight
            };

            var rect = TextMeasurer.MeasureSize(text, options);
            var location = new PointF(image.Width - rect.Width - TEXTPADDING, image.Height - rect.Height - TEXTPADDING);

            location = new PointF(30, 30 + yOffset);
            var locationBack = new PointF(35, 35 + yOffset);            

            image.Mutate(x => x.DrawText(text, font, Color.Black, locationBack)
                               .DrawText(text, font, Color.White, location));
        }
    }
}
