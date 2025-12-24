using System.Text.Json;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.Photo;

public class PhotoCacheService : IPhotoCacheService
{
    private static readonly JsonSerializerOptions IndexReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions IndexWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PhotoConfig _config;
    private readonly IPhotoDownloadService _downloadService;
    private readonly ILogger<PhotoCacheService> _logger;
    private readonly PhotoCacheIndex _index;
    private readonly string _cacheRoot;
    private readonly string _indexPath;
    private readonly object _indexSync = new();

    public PhotoCacheService(
        PhotoConfig config,
        IPhotoDownloadService downloadService,
        ILogger<PhotoCacheService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cacheRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(_config.CacheDirectory)
            ? "./photos"
            : _config.CacheDirectory);
        _indexPath = Path.Combine(_cacheRoot, "cache.json");

        Directory.CreateDirectory(_cacheRoot);
        _index = LoadIndex(_indexPath);
    }

    public bool IsCached(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var cacheKey = PhotoSourceDetector.NormalizeCacheKey(url);
        PhotoCacheEntry? entry;
        lock (_indexSync)
        {
            _index.Entries.TryGetValue(cacheKey, out entry);
        }

        if (entry == null)
            return false;

        var fullPath = ResolveLocalPath(entry.LocalPath);
        return File.Exists(fullPath);
    }

    public PhotoCacheEntry? GetEntry(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var cacheKey = PhotoSourceDetector.NormalizeCacheKey(url);
        lock (_indexSync)
        {
            if (!_index.Entries.TryGetValue(cacheKey, out var entry))
                return null;

            var updated = entry with { LastAccessedAt = DateTime.UtcNow };
            _index.Entries[cacheKey] = updated;
            return updated;
        }
    }

    public async Task<PhotoCacheEntry?> EnsureDownloadedAsync(string url, string personId)
    {
        return await EnsureDownloadedInternalAsync(url, personId, saveIndex: true);
    }

    public async Task<IReadOnlyList<PhotoCacheEntry>> EnsureDownloadedAsync(
        string personId,
        IEnumerable<string> urls)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return Array.Empty<PhotoCacheEntry>();

        var urlList = urls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (urlList.Count == 0)
            return Array.Empty<PhotoCacheEntry>();

        var maxConcurrency = Math.Max(1, _config.MaxConcurrentDownloads);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = urlList.Select(async url =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                return await EnsureDownloadedInternalAsync(url, personId, saveIndex: false).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var entries = results
            .Where(static r => r is not null)
            .Select(r => r!)
            .ToList();

        await SaveIndexAsync().ConfigureAwait(false);
        return entries;
    }

    public async Task<byte[]?> GetPhotoDataAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var cacheKey = PhotoSourceDetector.NormalizeCacheKey(url);
        PhotoCacheEntry? entry;
        lock (_indexSync)
        {
            if (!_index.Entries.TryGetValue(cacheKey, out entry))
                return null;

            var updated = entry with { LastAccessedAt = DateTime.UtcNow };
            _index.Entries[cacheKey] = updated;
            entry = updated;
        }

        var fullPath = ResolveLocalPath(entry.LocalPath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Cached photo missing on disk: {Path}", fullPath);
            return null;
        }

        return await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetPhotoDataByPathAsync(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return null;

        var fullPath = ResolveLocalPath(localPath);
        if (!File.Exists(fullPath))
            return null;

        TouchEntryByLocalPath(localPath);
        return await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
    }

    public void UpdateEntry(string url, string? contentHash, string? perceptualHash)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var cacheKey = PhotoSourceDetector.NormalizeCacheKey(url);
        lock (_indexSync)
        {
            if (!_index.Entries.TryGetValue(cacheKey, out var entry))
                return;

            var updated = entry with
            {
                ContentHash = contentHash ?? entry.ContentHash,
                PerceptualHash = perceptualHash ?? entry.PerceptualHash
            };

            _index.Entries[cacheKey] = updated;
        }
    }

    public void RecordMatch(string sourceUrl, string destinationUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(destinationUrl))
            return;

        var sourceCacheKey = PhotoSourceDetector.NormalizeCacheKey(sourceUrl);
        var destCacheKey = PhotoSourceDetector.NormalizeCacheKey(destinationUrl);
        var now = DateTime.UtcNow;

        lock (_indexSync)
        {
            // Update source entry with match info
            if (_index.Entries.TryGetValue(sourceCacheKey, out var sourceEntry))
            {
                var updated = sourceEntry with
                {
                    MatchedWithUrl = destCacheKey,
                    MatchedAt = now
                };
                _index.Entries[sourceCacheKey] = updated;
            }

            // Update destination entry with match info (bidirectional)
            if (_index.Entries.TryGetValue(destCacheKey, out var destEntry))
            {
                var updated = destEntry with
                {
                    MatchedWithUrl = sourceCacheKey,
                    MatchedAt = now
                };
                _index.Entries[destCacheKey] = updated;
            }
        }
    }

    public bool IsAlreadyMatched(string sourceUrl, string destinationUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(destinationUrl))
            return false;

        var sourceCacheKey = PhotoSourceDetector.NormalizeCacheKey(sourceUrl);
        var destCacheKey = PhotoSourceDetector.NormalizeCacheKey(destinationUrl);

        lock (_indexSync)
        {
            if (_index.Entries.TryGetValue(sourceCacheKey, out var sourceEntry))
            {
                if (string.Equals(sourceEntry.MatchedWithUrl, destCacheKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (_index.Entries.TryGetValue(destCacheKey, out var destEntry))
            {
                if (string.Equals(destEntry.MatchedWithUrl, sourceCacheKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public async Task SaveIndexAsync()
    {
        PhotoCacheIndex snapshot;
        lock (_indexSync)
        {
            snapshot = new PhotoCacheIndex
            {
                Version = _index.Version,
                Entries = new Dictionary<string, PhotoCacheEntry>(_index.Entries, StringComparer.OrdinalIgnoreCase)
            };
        }

        Directory.CreateDirectory(_cacheRoot);
        var json = JsonSerializer.Serialize(snapshot, IndexWriteOptions);
        var tempPath = _indexPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, _indexPath, true);
    }

    private PhotoCacheIndex LoadIndex(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return new PhotoCacheIndex
            {
                Entries = new Dictionary<string, PhotoCacheEntry>(StringComparer.OrdinalIgnoreCase)
            };
        }

        try
        {
            var json = File.ReadAllText(indexPath);
            var index = JsonSerializer.Deserialize<PhotoCacheIndex>(json, IndexReadOptions)
                        ?? new PhotoCacheIndex();

            index.Entries ??= new Dictionary<string, PhotoCacheEntry>();
            index.Entries = new Dictionary<string, PhotoCacheEntry>(index.Entries, StringComparer.OrdinalIgnoreCase);
            return index;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load photo cache index, starting fresh.");
            return new PhotoCacheIndex
            {
                Entries = new Dictionary<string, PhotoCacheEntry>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    private async Task<PhotoCacheEntry?> EnsureDownloadedInternalAsync(
        string url,
        string personId,
        bool saveIndex)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(personId))
            return null;

        var cacheKey = PhotoSourceDetector.NormalizeCacheKey(url);
        var cached = GetCachedEntryIfPresent(cacheKey, personId);
        if (cached != null)
        {
            // If same URL is used by multiple persons, create a person-specific entry pointing to same file
            if (cached.PersonId != personId)
            {
                var photoSource = PhotoSourceDetector.DetectSource(url);
                var newRelativePath = BuildRelativePath(photoSource, personId, Path.GetFileName(cached.LocalPath), "");
                var newFullPath = ResolveLocalPath(newRelativePath);
                var oldFullPath = ResolveLocalPath(cached.LocalPath);

                // Create directory and copy/link file if needed
                Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
                if (!File.Exists(newFullPath) && File.Exists(oldFullPath))
                {
                    File.Copy(oldFullPath, newFullPath, overwrite: false);
                }

                // Create new entry for this person
                var personEntry = cached with
                {
                    LocalPath = newRelativePath,
                    PersonId = personId
                };

                lock (_indexSync)
                {
                    // Store under composite key: cacheKey|personId
                    _index.Entries[$"{cacheKey}|{personId}"] = personEntry;
                }

                return personEntry;
            }

            return cached;
        }

        var downloadResult = await DownloadWithRetriesAsync(url).ConfigureAwait(false);
        if (downloadResult == null || downloadResult.Data == null || downloadResult.Data.Length == 0)
        {
            _logger.LogWarning("Failed to download photo: {Url}", url);
            return null;
        }

        var source = PhotoSourceDetector.DetectSource(url);
        var relativePath = BuildRelativePath(source, personId, downloadResult.FileName, downloadResult.ContentType);
        var fullPath = ResolveLocalPath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, downloadResult.Data).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var entry = new PhotoCacheEntry
        {
            Url = cacheKey, // Store normalized URL
            LocalPath = relativePath,
            PersonId = personId,
            Source = source,
            FileSize = downloadResult.Data.Length,
            ContentHash = null,
            PerceptualHash = null,
            DownloadedAt = now,
            LastAccessedAt = now
        };

        lock (_indexSync)
        {
            _index.Entries[cacheKey] = entry;
        }

        if (saveIndex)
        {
            await SaveIndexAsync().ConfigureAwait(false);
        }

        return entry;
    }

    private PhotoCacheEntry? GetCachedEntryIfPresent(string cacheKey, string? personId = null)
    {
        lock (_indexSync)
        {
            // First try composite key cacheKey|personId if personId provided
            if (!string.IsNullOrWhiteSpace(personId) &&
                _index.Entries.TryGetValue($"{cacheKey}|{personId}", out var personEntry))
            {
                var personFullPath = ResolveLocalPath(personEntry.LocalPath);
                if (File.Exists(personFullPath))
                {
                    var updatedPerson = personEntry with { LastAccessedAt = DateTime.UtcNow };
                    _index.Entries[$"{cacheKey}|{personId}"] = updatedPerson;
                    return updatedPerson;
                }
            }

            // Fall back to cacheKey-only key
            if (!_index.Entries.TryGetValue(cacheKey, out var entry))
                return null;

            var fullPath = ResolveLocalPath(entry.LocalPath);
            if (!File.Exists(fullPath))
                return null;

            var updated = entry with { LastAccessedAt = DateTime.UtcNow };
            _index.Entries[cacheKey] = updated;
            return updated;
        }
    }

    private void TouchEntryByLocalPath(string localPath)
    {
        var relativePath = TryGetRelativePath(localPath);
        if (relativePath == null)
            return;

        var normalized = NormalizeRelativePath(relativePath);

        lock (_indexSync)
        {
            foreach (var pair in _index.Entries)
            {
                if (!PathsEqual(pair.Value.LocalPath, normalized))
                    continue;

                var updated = pair.Value with { LastAccessedAt = DateTime.UtcNow };
                _index.Entries[pair.Key] = updated;
                break;
            }
        }
    }

    private string ResolveLocalPath(string localPath)
    {
        return Path.IsPathRooted(localPath)
            ? localPath
            : Path.Combine(_cacheRoot, localPath);
    }

    private string? TryGetRelativePath(string localPath)
    {
        if (!Path.IsPathRooted(localPath))
            return localPath;

        var fullPath = Path.GetFullPath(localPath);
        var rootPath = Path.GetFullPath(_cacheRoot);
        if (!rootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            rootPath += Path.DirectorySeparatorChar;
        }

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.GetRelativePath(rootPath, fullPath);
    }

    private static string BuildRelativePath(
        string source,
        string personId,
        string fileName,
        string contentType)
    {
        var safeSource = SanitizePathSegment(source);
        var safePersonId = SanitizePathSegment(personId);
        var safeFileName = SanitizeFileName(fileName, contentType);

        return Path.Combine(safeSource, safePersonId, safeFileName);
    }

    private static string SanitizeFileName(string fileName, string contentType)
    {
        var cleanName = SanitizePathSegment(Path.GetFileName(fileName));
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName == "." || cleanName == "..")
        {
            return GetFallbackFileName(contentType);
        }

        var extension = Path.GetExtension(cleanName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            cleanName += GetExtensionForContentType(contentType);
        }

        return cleanName;
    }

    private static string SanitizePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(segment
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        cleaned = cleaned.Trim().Trim('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    private static string GetFallbackFileName(string contentType)
    {
        return $"photo-{Guid.NewGuid():N}{GetExtensionForContentType(contentType)}";
    }

    private static string GetExtensionForContentType(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }

    private static string NormalizeRelativePath(string path)
    {
        var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static bool PathsEqual(string path1, string path2)
    {
        var normalized1 = NormalizeRelativePath(path1);
        var normalized2 = NormalizeRelativePath(path2);
        return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PhotoDownloadResult?> DownloadWithRetriesAsync(string url)
    {
        if (!_downloadService.IsSupportedPhotoUrl(url))
        {
            _logger.LogWarning("Skipping unsupported photo URL: {Url}", url);
            return null;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await _downloadService.DownloadPhotoAsync(url).ConfigureAwait(false);
            if (result != null)
                return result;

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Retrying photo download ({Attempt}/{Max}) after {Delay}s: {Url}",
                    attempt + 1, maxAttempts, delay.TotalSeconds, url);
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        return null;
    }
}
