

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGetter.Models
{
    public class Location
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Address { get; set; }
        public bool? AddressAltered { get; set; }

        public bool IsValid() => Latitude.HasValue && Longitude.HasValue;

        internal double? DistanceTo(Location otherLocation)
        {
            if(IsValid() && otherLocation.IsValid())
                return CalculateDistance(Latitude!.Value, Longitude!.Value, otherLocation.Latitude!.Value, otherLocation.Longitude!.Value);

            return null;
        }        

        // Radius of Earth in kilometers. Use 3958.8 for miles.
        private const double EarthRadiusKm = 6371.0;

        /// <summary>
        /// Calculates the distance between two points on Earth using the Haversine formula.
        /// </summary>
        /// <param name="lat1">Latitude of point 1 in decimal degrees</param>
        /// <param name="lon1">Longitude of point 1 in decimal degrees</param>
        /// <param name="lat2">Latitude of point 2 in decimal degrees</param>
        /// <param name="lon2">Longitude of point 2 in decimal degrees</param>
        /// <returns>Distance in kilometers</returns>
        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {            
            // Validate latitude and longitude ranges
            if (lat1 < -90 || lat1 > 90 || lat2 < -90 || lat2 > 90 ||
                lon1 < -180 || lon1 > 180 || lon2 < -180 || lon2 > 180)
            {
                throw new ArgumentOutOfRangeException("Latitude must be between -90 and 90, longitude between -180 and 180.");
            }

            // Convert degrees to radians
            double lat1Rad = DegreesToRadians(lat1);
            double lon1Rad = DegreesToRadians(lon1);
            double lat2Rad = DegreesToRadians(lat2);
            double lon2Rad = DegreesToRadians(lon2);

            // Haversine formula
            double dLat = lat2Rad - lat1Rad;
            double dLon = lon2Rad - lon1Rad;

            double a = Math.Pow(Math.Sin(dLat / 2), 2) +
                       Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                       Math.Pow(Math.Sin(dLon / 2), 2);

            double c = 2 * Math.Asin(Math.Sqrt(a));

            return Math.Abs(EarthRadiusKm * c); // Distance in kilometers
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        internal static Location? Parse(string homeLocation)
        {
            return JsonSerializer.Deserialize<Location>(homeLocation);
        }
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