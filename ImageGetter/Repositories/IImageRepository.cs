using ImageGetter.Models;

namespace ImageGetter.Repositories
{
    public interface IImageRepository
    {
        void AddMedia(MediaMeta media);
        bool TryGetMedia(string filename, out MediaMeta mediaMeta);
    }
}