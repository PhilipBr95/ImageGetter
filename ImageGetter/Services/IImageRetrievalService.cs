
using ImageGetter.Models;

namespace ImageGetter.Services
{
    public interface IImageRetrievalService
    {
        MediaFile? GetImage(string path);
        Media? GetImage(int mediaId);
        IEnumerable<Media> GetImages();
        Media GetRandomImage();
    }
}