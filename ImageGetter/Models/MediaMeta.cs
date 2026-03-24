

namespace ImageGetter.Models
{
    public class Location
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Address { get; set; }
    }

    public class Objects
    {
        public int DetectionVersion { get; set; }
        public IEnumerable<Face>? Faces { get; set; }

        internal void SetFaces(int version, IEnumerable<Face>? faces)
        {
            DetectionVersion = version;
            Faces = faces;
        }

        internal IEnumerable<Face>? GetObjects(ObjectTypes objectTypes)
        {
            return objectTypes.HasFlag(ObjectTypes.Faces) ? Faces : null;
        }

        internal bool HasObjects(int version)
        {
            return DetectionVersion == version;
        }
    }

    [Flags]
    public enum ObjectTypes
    {
        Faces = 1,
    }

    public class MediaMeta
    {
        public string Filename { get; set; }
        public Location Location { get; set; }
        public int DisplayCount { get; set; } = 0;
        public DateTime LastViewedDate { get; set; }
        public int MediaId { get; set; }
        public Objects Objects { get; set; } = new Objects();

        internal string? GetLocation() => Location?.Address;
                
    }
}