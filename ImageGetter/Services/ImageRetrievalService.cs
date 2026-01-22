using ImageGetter.Extensions;
using ImageGetter.Models;
using ImageGetter.Repositories;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Web;

namespace ImageGetter.Services
{
    internal class ImageRetrievalService : IImageRetrievalService
    {
        private readonly IImageRepository _imageRepository;
        private Settings _settings;
        private List<Media> _media;
        private readonly ILogger<ImageRetrievalService> _logger;

        public ImageRetrievalService(IImageRepository imageRepository, IOptions<Settings> settings, ILogger<ImageRetrievalService> logger)
        {
            _imageRepository = imageRepository;
            _settings = settings.Value;
            _logger = logger;
        }

        public void LoadImages()
        {
            try
            {
                using SftpClient client = new SftpClient(new PasswordConnectionInfo(_settings.Host, _settings.Username, _settings.ImagePassword));
                client.Connect();

                var allMedia = new List<Media>();

                foreach (var path in _settings.Paths)
                {
                    if (client.Exists(path))
                    {
                        var images = client.ListDirectory(path)
                                           .Where(i => !i.IsDirectory && i.FullName.ToLower()
                                                                                   .EndsWith("jpg"))
                                           .OrderBy(i => i.LastWriteTimeUtc)
                                           .ThenByDescending(i => i.FullName)
                                           .Select(s => new Media { Filename = s.FullName, Id = HttpUtility.UrlEncode(s.FullName), LastWriteTimeUtc = s.LastWriteTimeUtc });

                        allMedia.AddRange(images);
                        _logger.LogInformation($"Found {images.Count()} images in {path}");
                    }
                }

                client.Disconnect();

                _logger.LogInformation($"Total images Found: {allMedia.Count}");
                
                _media = allMedia.OrderBy(o => o.LastWriteTimeUtc)
                                 .ThenBy(o => o.Filename)
                                 .Select((s, i) => new Media { MediaId = i, Filename = s.Filename, LastWriteTimeUtc = s.LastWriteTimeUtc, Id = s.Id })
                                 .ToList();

                //foreach (var d in _media)
                //{
                //   _logger.LogInformation($"{d.MediaId}, {d.Filename}");
                //}

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get images from {_settings.Host}");
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
            int mediaId = _media?.FirstOrDefault(m => m.Filename == path)?.MediaId ?? -1;

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

                if (_imageRepository.TryGetMedia(path, out MediaMeta mediaMeta))
                {                    
                    location = mediaMeta?.Location?.Address;
                    _logger.LogInformation($"Using cached location: {location}");
                }
                else if (Path.GetExtension(path) == ".jpg")
                        location = image.Metadata.GetLocationStringAsync().GetAwaiter().GetResult() ?? "";
            }
            else
            {
                _logger.LogWarning($"No EXIF data found for {path}");
            }

            _imageRepository.AddMedia(new MediaMeta
            {
                Filename = path,
                Location = new Location
                {
                    Address = location,
                    Latitude = image.Metadata?.GetExifLatitude(),
                    Longitude = image.Metadata?.GetExifLongitude()
                },
                MediaId = mediaId       //Mainly for current logging purposes
            });

            return new MediaFile
            {
                Filename = path,
                Data = memoryStream.ToArray(),
                Width = image.Width,
                Height = image.Height,
                CreatedDate = createdDate,
                Location = location,
                Orientation = orientation,
                MediaId = mediaId
            };
        }

        public Media GetRandomImage()
        {
            if (_media == null || !_media.Any())
                LoadImages();

            var random = new Random();
            var index = random.Next(0, _media.Count - 1);

            return _media[index];
        }

        public Media? GetImage(int mediaId)
        {
            return _media.FirstOrDefault(f => f.MediaId == mediaId);
        }
    }
}
