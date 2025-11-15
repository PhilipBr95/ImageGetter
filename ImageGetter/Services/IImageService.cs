using SixLabors.ImageSharp;

namespace ImageGetter.Services
{
    public interface IImageService
    {
        Task CacheImageAsync(int? width = null, int? height = null);
        Task<Image?> GetCachedImageAsync(int? width = null, int? height = null);
        Task<Image?> RetrieveImageAsync(string? filename = null, int? width = null, int? height = null, bool debug = false);
    }
}