using Geocoding;
using Geocoding.Google;
using ImageGetter.Models;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageGetter.Extensions
{
    public static class ImageMetadataExtensions
    {
        private static ILogger _logger;
        private static Settings _settings;

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ImageMetadataExtensions));
            _settings = serviceProvider.GetRequiredService<IOptions<Settings>>().Value;
        }

        static async public Task DebugExif(this ImageMetadata metadata)
        {
            foreach(var val in metadata.ExifProfile.Values)
            {
                var value = val.GetValue();
                _logger.LogDebug($"{val.Tag}, {val.IsArray}: {value}");
            }
        }

        static public ushort? GetOrientation(this ImageMetadata metadata)
        {
            if(!metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out IExifValue<ushort>? orientationRefValue))
                return null;

            return orientationRefValue.Value;
        }
        
        static async public Task<string?> GetLocationStringAsync(this ImageMetadata metadata)
        {
            var latitude = GetExifLatitude(metadata);
            var longitude = GetExifLongitude(metadata);

            if (latitude.HasValue && longitude.HasValue)
            {
                var location = new Location(latitude.Value, longitude.Value);

                IGeocoder geocoder = new GoogleGeocoder() { ApiKey = _settings.GoogleApiKey };
                IEnumerable<Address> addresses = await geocoder.ReverseGeocodeAsync(location);
                
                foreach(var address in addresses)
                {
                    var locationTypeProp = address.GetType().GetProperty("LocationType");
                    var locationType = locationTypeProp.GetValue(address);

                    _logger.LogDebug($"{locationType}, {address.FormattedAddress}");
                }

                return addresses.First().FormattedAddress;                
            }            

            return null;
        }

        static public double? GetExifLongitude(this ImageMetadata metadata)
        {
            if (metadata.ExifProfile?.TryGetValue(ExifTag.GPSLongitude, out IExifValue<Rational[]>? longitudeParts) == true && longitudeParts?.Value?.Length == 3)
            {
                uint degrees = longitudeParts.Value[0].Numerator;
                double minutes = longitudeParts.Value[1].Numerator / 60D;
                double seconds = (longitudeParts.Value[2].Numerator / (double)longitudeParts.Value[2].Denominator) / 3600D;
                var coord = degrees + minutes + seconds;

                if (metadata.ExifProfile.TryGetValue(ExifTag.GPSLongitudeRef, out IExifValue<string>? longitudeRefValue) && longitudeRefValue?.Value is string longitudeRef)
                {
                    if (longitudeRef == "S" || longitudeRef == "W")
                        coord = 0 - coord;
                }

                return coord;
            }

            return null;
        }

        static public double? GetExifLatitude(this ImageMetadata metadata)
        {
            if (metadata.ExifProfile?.TryGetValue(ExifTag.GPSLatitude, out IExifValue<Rational[]>? latitudeParts) == true && latitudeParts?.Value?.Length == 3)
            {
                uint degrees = latitudeParts.Value[0].Numerator;
                double minutes = latitudeParts.Value[1].Numerator / 60D;
                double seconds = (latitudeParts.Value[2].Numerator / (double)latitudeParts.Value[2].Denominator) / 3600D;
                var coord = degrees + minutes + seconds;

                if (metadata.ExifProfile.TryGetValue(ExifTag.GPSLatitudeRef, out IExifValue<string>? latitudeRefValue) && latitudeRefValue?.Value is string latitudeRef)
                {
                    if (latitudeRef == "S" || latitudeRef == "W")
                        coord = 0 - coord;
                }

                return coord;
            }

            return null;
        }
    }
}
