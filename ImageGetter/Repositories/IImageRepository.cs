using ImageGetter.Models;

namespace ImageGetter.Repositories
{
    public interface IImageRepository
    {
        void AddMedia(MediaMeta media);
        void IncrementDisplayCount(string filename);
        bool TryGetMedia(string filename, out MediaMeta mediaMeta);
    }
}