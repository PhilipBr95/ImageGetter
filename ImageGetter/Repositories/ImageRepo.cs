using ImageGetter.Models;
using Microsoft.Extensions.Options;
using System.Runtime;
using System.Text.Json;

namespace ImageGetter.Repositories
{
    public class ImageRepo : IImageRepository, IDisposable
    {
        private readonly Settings _settings;
        private readonly ILogger<IImageRepository> _logger;
        private List<MediaMeta> _db;
        
        private int _pendingSaveCount = 0;

        public ImageRepo(IOptions<Settings> settings, ILogger<IImageRepository> logger) 
        {
            _settings = settings.Value;
            _logger = logger;

            //Force a quick save to help with debugging
            _pendingSaveCount = _settings.MaxCachedSaves - 1;

            _db = LazyInitializer.EnsureInitialized(ref _db, () => LoadDatabase());
        }

        private List<MediaMeta>? LoadDatabase()
        {
            _logger.LogInformation("Loading image database from disk");

            if (!File.Exists(_settings.DatabasePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settings.DatabasePath)!);

                _logger.LogWarning($"Image database file not found, creating new database {_settings.DatabasePath}");

                _db = [];
                SaveDatabase(true);

                return _db;
            }

            var json = File.ReadAllText(_settings.DatabasePath);
            var db = JsonSerializer.Deserialize<List<MediaMeta>>(json);

            _logger.LogInformation($"Loaded {db?.Count ?? 0} media entries from database");
            _logger.LogInformation($"First entry: {JsonSerializer.Serialize(db.FirstOrDefault())}");

            return db;
        }

        public bool TryGetMedia(string filename, out MediaMeta mediaMeta)
        {
            mediaMeta = _db.FirstOrDefault(m => m.Filename == filename);
            IncrementDisplayCount(mediaMeta);

            return mediaMeta != null;
        }

        public void AddMedia(MediaMeta media) 
        {
            try
            {
                IncrementDisplayCount(media);
                _db.Add(media);

                SaveDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding media to database: {JsonSerializer.Serialize(media)}");
            }
        }

        public void UpdateMedia(MediaMeta media)
        {
            if(TryGetMedia(media.Filename, out MediaMeta mediaMeta))
                _db.Remove(mediaMeta);

            AddMedia(media);
        }

        public void IncrementDisplayCount(string filename)
        {
            if (TryGetMedia(filename, out MediaMeta mediaMeta))
            {
                IncrementDisplayCount(mediaMeta);
            }
        }

        public void IncrementDisplayCount(MediaMeta mediaMeta)
        {
            if (mediaMeta is null)
                return;

            mediaMeta.DisplayCount++;
            mediaMeta.LastViewedDate = DateTime.UtcNow;
        }

        private void SaveDatabase(bool forceSave = false)
        {
            _pendingSaveCount++;

            if (forceSave || _pendingSaveCount >= _settings.MaxCachedSaves)
            {
                _logger.LogInformation("Saving image database({_db.Count} images) to disk");
                
                var json = JsonSerializer.Serialize(_db, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settings.DatabasePath, json);

                _pendingSaveCount = 0;
            }
        }

        public void Dispose()
        {
            if(_db is not null)
                SaveDatabase(true);

            GC.SuppressFinalize(this);
        }
    }
}
