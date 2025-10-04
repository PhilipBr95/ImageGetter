using ImageGetter.Services;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Index()
        {
            var image = _imageService.GetRandomImage();
            var file = _imageService.GetImage(image.Filename);

            return (IActionResult)File(file.Data, "image/jpeg" , image.Id);
        }
    }
}
