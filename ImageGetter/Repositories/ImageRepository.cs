using ImageGetter.Models;
using Microsoft.Extensions.Options;
using System.Runtime;
using System.Text.Json;

namespace ImageGetter.Repositories
{
    public class ImageRepository : IImageRepository, IDisposable
    {
        private readonly Settings _settings;
        private readonly ILogger<IImageRepository> _logger;
        private List<MediaMeta> _db;
        
        private int _pendingSaveCount = 0;
        private int _pendingBackupCount = 0;

        public ImageRepository(IOptions<Settings> settings, ILogger<IImageRepository> logger) 
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

            var totalViews = db?.GroupBy(m => m.Filename)
                                .Select(s => new { Filename = s.Key, DisplayCount = s.Sum(ss => ss.DisplayCount) })
                                .ToList();

            //Remove dupes
            var fixedDb = db?.GroupBy(m => m.Filename)
                             .Select(g => g.Last())
                             .ToList();

            _logger.LogInformation($"Removed {db?.Count - fixedDb?.Count} duplicate entries from database [{db?.Count} vs {fixedDb?.Count}]");

            if (_settings.HomeLocation is not null)
                FixHomeLocationNearMisses(fixedDb);

            //Fix display counts to be the total views across all duplicates
            fixedDb.ForEach(m => m.DisplayCount = totalViews?.FirstOrDefault(t => t.Filename == m.Filename)?.DisplayCount ?? m.DisplayCount);

            _logger.LogInformation($"Loaded {fixedDb?.Count ?? 0} media entries from database");
            _logger.LogInformation($"Most Popular: {fixedDb?.Max(o => o.DisplayCount)} views");
            _logger.LogInformation($"Images with Cached Objects: {fixedDb?.Count(w => w.Objects.DetectionVersion > 0)}");

            return fixedDb;
        }

        /// <summary>
        /// Fix any near misses related to the home location by checking if the distance is within the HomeLocationProximity threshold 
        /// </summary>
        /// <param name="fixedDb"></param>
        private void FixHomeLocationNearMisses(List<MediaMeta>? fixedDb)
        {
            var nearMissCount = 0;

            //Fix any near misses related to the home location
            fixedDb?.ForEach(m =>
            {
                if (m.Location is not null)
                {
                    if (m.Location.Address != _settings.HomeLocation.Address)
                    {
                        var distance = m.Location.DistanceTo(_settings.HomeLocation);
                        if (distance > 0 && distance < _settings.HomeLocationProximity)
                        {
                            _logger.LogInformation($"Updating location for {m.MediaId} from {m.Location.Address} to [HOME] due to proximity {distance:F4}km");
                            m.Location.Address = _settings.HomeLocation.Address;
                            m.Location.AddressAltered = true;

                            nearMissCount++;
                        }
                    }
                }
            });

            _logger.LogInformation($"Fixed {nearMissCount} near misses related to home location");
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding media to database: {JsonSerializer.Serialize(media)}");
            }
        }

        private void IncrementDisplayCount(MediaMeta? mediaMeta)
        {
            if (mediaMeta is null)
                return;

            mediaMeta.DisplayCount++;
            mediaMeta.LastViewedDate = DateTime.UtcNow;

            SaveDatabase();
        }

        private void SaveDatabase(bool forceSave = false)
        {
            _pendingSaveCount++;
            _pendingBackupCount++;

            if (_settings.DebugMode || forceSave || _pendingSaveCount >= _settings.MaxCachedSaves)
            {
                _logger.LogInformation($"Saving image database({_db.Count} images) to disk");
                                
                var json = JsonSerializer.Serialize(_db, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settings.DatabasePath, json);

                _pendingSaveCount = 0;

                //Do we need to create a backup
                if (_pendingBackupCount >= _settings.BackupEvery)
                {
                    var backupPath = Path.Combine(Path.GetDirectoryName(_settings.DatabasePath), "Backup");
                    Directory.CreateDirectory(backupPath);

                    backupPath = Path.Combine(backupPath, $"Images.{DateTime.UtcNow:yyyyMMddHHmmss}.bak");                    
                    File.WriteAllText(backupPath, json);

                    _logger.LogInformation($"Saving image backup to {backupPath}");
                    _pendingBackupCount = 0;
                }
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
