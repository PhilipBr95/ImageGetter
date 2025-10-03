
using ImageGetter.Models;

namespace ImageGetter.Services
{
    public interface IImageService
    {
        MediaFile? GetImage(string path);
        IEnumerable<Media> GetImages();
        Media GetRandomImage();
    }
}