
using ImageGetter.Models;

namespace ImageGetter.Services
{
    public interface IImageRetrievalService
    {
        MediaFile? GetImage(string path);
        IEnumerable<Media> GetImages();
        Media GetRandomImage();
    }
}