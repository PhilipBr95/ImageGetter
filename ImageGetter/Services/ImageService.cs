using ImageGetter.Extensions;
using ImageGetter.Models;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Web;

namespace ImageGetter.Services
{
    internal class ImageService : IImageService
    {
        private Settings _settings;
        private List<Media> _media;
        private readonly ILogger<ImageService> _logger;

        public ImageService(IOptions<Settings> settings, ILogger<ImageService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public IEnumerable<Media> GetImages()
        {
            try
            {
                using SftpClient client = new SftpClient(new PasswordConnectionInfo(_settings.Host, _settings.Username, _settings.ImagePassword));
                client.Connect();

                _media = new List<Media>();

                foreach (var path in _settings.Paths)
                {
                    if (client.Exists(path))
                    {
                        _media.AddRange(client.ListDirectory(path)
                                             .Where(i => !i.IsDirectory && i.FullName.EndsWith("jpg"))
                                             .Select(s => new Media { Filename = s.FullName, Id = HttpUtility.UrlEncode(s.FullName) }));
                    }
                }

                client.Disconnect();

                _logger.LogInformation($"Found {_media.Count} images");
                return _media;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get images");
                throw;
            }
        }

        public MediaFile? GetImage(string path)
        {
            using SftpClient client = new SftpClient(new PasswordConnectionInfo(_settings.Host, _settings.Username, _settings.ImagePassword));
            client.Connect();
            
            if (!client.Exists(path))
            {
                _logger.LogError($"Failed to find {path}");
                return null;
            }

            _logger.LogInformation($"Downloading {path}");

            using var memoryStream = new MemoryStream();
            client.DownloadFile(path, memoryStream);

            var image = Image.Load(memoryStream.ToArray());

            DateTime createdDate = DateTime.MinValue;
            string location = "";
            ushort orientation = 0;

            //image.Metadata.DebugExif();

            var exifProfile = image.Metadata.ExifProfile;
            if (exifProfile != null)
            {
                if (exifProfile.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.DateTimeOriginal, out var exifValue))
                {
                    var dateString = exifValue?.GetValue() as string;
                    if (!string.IsNullOrEmpty(dateString))
                    {
                        if (DateTime.TryParse(dateString, out DateTime parsedDate))
                            createdDate = parsedDate;
                        if (DateTime.TryParseExact(dateString, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                            createdDate = parsedDate;
                    }
                }

                orientation = image.Metadata.GetOrientation() ?? 0;
                location = image.Metadata.GetLocationStringAsync().GetAwaiter().GetResult() ?? "";
            }

            return new MediaFile
            {
                Filename = path,
                Data = memoryStream.ToArray(),
                Width = image.Width,
                Height = image.Height,
                CreatedDate = createdDate,
                Location = location,
                Orientation = orientation
            };
        }

        public Media GetRandomImage()
        {
            if (_media == null || !_media.Any())
                GetImages();

            var random = new Random();
            var index = random.Next(0, _media.Count - 1);

            return _media[index];
        }
    }
}
