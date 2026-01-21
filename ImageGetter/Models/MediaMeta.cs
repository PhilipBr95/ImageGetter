

namespace ImageGetter.Models
{
    public class Location
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Address { get; set; }


    }

    public class MediaMeta
    {
        public string Filename { get; set; }
        public Location Location { get; set; }
        public int DisplayCount { get; set; } = 0;
        public DateTime LastViewedDate { get; set; }
        public int MediaId { get; set; }
    }
}