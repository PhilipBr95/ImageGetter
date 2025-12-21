using ImageGetter.Models;
using SixLabors.ImageSharp;

namespace ImageGetter.Services
{
    public interface IImageService
    {
        Task CacheImageAsync(int? width = null, int? height = null, bool debug = false);
        Task<ImageWithMeta?> GetCachedImageAsync(int? width = null, int? height = null, bool debug = false);
        Task<ImageWithMeta?> RetrieveImageAsync(string filename, int? width = null, int? height = null, bool debug = false);
        Task<ImageWithMeta?> RetrieveImageAsync(int mediaId, int? width = null, int? height = null, bool debug = false);
    }
}