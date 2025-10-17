using ImageGetter.Models;
using ImageGetter.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ImageGetter.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly IImageService _imageService;
        private readonly ILogger<ImageController> _logger;

        public ImageController(IImageService imageService, ILogger<ImageController> logger) 
        {
            _imageService = imageService;
            _logger = logger;
        }

        [HttpHead]
        public IActionResult Head()
        {
            Response.Headers.LastModified = DateTimeOffset.Now.ToString("ddd, d MMM yyyy HH:mm:ss");
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetImage()
        {
            return await GetImage(null, null, null);
        }

        [HttpGet("/image/{filename}")]
        public async Task<IActionResult> GetImage(string filename)
        {
            return await GetImage(filename, null, null);
        }

        [HttpGet("/image/{width:int}/{height:int}")]
        public async Task<IActionResult> GetImage(int width, int height)
        {
            return await GetImage(null, width, height);
        }


        [HttpGet("/image/{filename}/{width:int?}/{height:int?}")]
        public async Task<IActionResult> GetImage(string? filename, int? width, int? height)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                var media = _imageService.GetRandomImage();
                filename = media.Filename;
            }           

            var file = _imageService.GetImage(filename);
            if (file == null)
            {
                _logger.LogError($"Failed to find image {filename}");
                return NotFound(filename);
            }

            var image = await Image.LoadAsync(new MemoryStream(file.Data));
            image.Mutate(x => x.AutoOrient());

            if (width != null)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(width ?? image.Width, height ?? image.Height),
                    Mode = ResizeMode.Max
                }));
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
