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
        public async Task<IActionResult> Index(string? filename)
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

            var landscape = file.IsLandscape;
            _logger.LogDebug($"{(landscape ? "Landscape mode" : "Portrate mode")} - Orientation:{file.Orientation} - Dimensions:{image.Width}x{image.Height}");

            AddText(file.ParentFolderName, image, 0, landscape);

            var createdDate = file.CreatedDate.ToString("dd/MMM/yyyy");
            AddText(createdDate, image, 100, landscape);

            var location = file.Location;
            AddText(location, image, 200, landscape);

            MemoryStream ms = new();
            image.Save(ms, new JpegEncoder());
            return File(ms.ToArray(), "image/jpeg");
        }

        private void AddText(string text, Image image, int yOffset, bool landscape)
        {
            const float TEXTPADDING = 18f;
            const string TEXTFONT = "Calibri";

            float textFontSize = 100f;

            if (landscape)
            {
                textFontSize += 50f;
                yOffset += 30;
            }

            if (!SystemFonts.TryGet(TEXTFONT, out FontFamily fontFamily))
                throw new Exception($"Couldn't find font {TEXTFONT}");

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
