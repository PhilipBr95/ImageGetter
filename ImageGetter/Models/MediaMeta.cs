
namespace ImageGetter.Models
{
    public class MediaMeta
    {
        public string Filename { get; set; }
        public string Location { get; set; }
        public int DisplayCount { get; set; } = 0;
        public DateTime LastViewedDate { get; set; }
        public int MediaId { get; set; }
    }
}