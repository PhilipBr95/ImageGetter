using ImageGetter.Models;
using ImageGetter.Services;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

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
            return await GetImage(null, null, filename);
        }

        [HttpGet("/image/{width:int}/{height:int}")]
        public async Task<IActionResult> GetImage(int width, int height)
        {
            return await GetImage(width, height, null);
        }

        [HttpGet("/image/{width}/{height}/{filename}")]
        public async Task<IActionResult> GetImage(int? width = null, int? height = null, string? filename = null)
        {
            ImageWithMeta? image;
            if (string.IsNullOrWhiteSpace(filename) || filename == "random.jpg")
            {
                _logger.LogInformation("GetImage: No parameters specified, returning cached image");

                image = await _imageService.GetCachedImageAsync(width, height);
            }
            else
            {
                //Can't figure out optional params in routing :-(
                _ = bool.TryParse(Request.Query.Where(f => f.Key.Equals("debug", StringComparison.CurrentCultureIgnoreCase))
                                               .FirstOrDefault().Value, out bool debug);

                image = await _imageService.RetrieveImageAsync(filename, width, height, debug);
            }

            if(image == null)
                return NotFound(filename);

            MemoryStream ms = new();
            image.Image.Save(ms, new JpegEncoder());
            return File(ms.ToArray(), "image/jpeg");
        }
    }
}
