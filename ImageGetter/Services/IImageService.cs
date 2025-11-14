using SixLabors.ImageSharp;

namespace ImageGetter.Services
{
    public interface IImageService
    {
        Task CacheImageAsync();
        Task<Image?> GetCachedImageAsync();
        Task<Image?> RetrieveImageAsync(string? filename = null, int? width = null, int? height = null, bool debug = false);
    }
}