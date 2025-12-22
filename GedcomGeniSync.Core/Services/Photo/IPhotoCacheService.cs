using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.Photo;

public interface IPhotoCacheService
{
    /// <summary>
    /// Check if a photo is present in cache and available on disk.
    /// </summary>
    bool IsCached(string url);

    /// <summary>
    /// Get cache entry by URL.
    /// </summary>
    PhotoCacheEntry? GetEntry(string url);

    /// <summary>
    /// Download a photo and add it to cache if missing.
    /// </summary>
    Task<PhotoCacheEntry?> EnsureDownloadedAsync(string url, string personId);

    /// <summary>
    /// Download multiple photos for a person.
    /// </summary>
    Task<IReadOnlyList<PhotoCacheEntry>> EnsureDownloadedAsync(
        string personId,
        IEnumerable<string> urls);

    /// <summary>
    /// Get cached photo data by URL.
    /// </summary>
    Task<byte[]?> GetPhotoDataAsync(string url);

    /// <summary>
    /// Get cached photo data by local path (relative to cache root or absolute).
    /// </summary>
    Task<byte[]?> GetPhotoDataByPathAsync(string localPath);

    /// <summary>
    /// Update cache entry with computed hashes.
    /// </summary>
    void UpdateEntry(string url, string? contentHash, string? perceptualHash);

    /// <summary>
    /// Persist cache index to disk.
    /// </summary>
    Task SaveIndexAsync();
}
