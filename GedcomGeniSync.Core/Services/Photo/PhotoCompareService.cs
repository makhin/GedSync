using System.Globalization;
using System.Linq;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.Photo;

public class PhotoCompareService : IPhotoCompareService
{
    private readonly PhotoConfig _config;
    private readonly IPhotoCacheService _photoCacheService;
    private readonly IPhotoHashService _photoHashService;
    private readonly ILogger<PhotoCompareService> _logger;
    private readonly double _similarityThreshold;

    public PhotoCompareService(
        PhotoConfig config,
        IPhotoCacheService photoCacheService,
        IPhotoHashService photoHashService,
        ILogger<PhotoCompareService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _photoCacheService = photoCacheService ?? throw new ArgumentNullException(nameof(photoCacheService));
        _photoHashService = photoHashService ?? throw new ArgumentNullException(nameof(photoHashService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _similarityThreshold = Math.Clamp(_config.SimilarityThreshold, 0.0, 1.0);
    }

    public async Task<PhotoCompareReport> ComparePersonPhotosAsync(
        string sourcePersonId,
        IReadOnlyList<string> sourcePhotoUrls,
        string destinationPersonId,
        IReadOnlyList<string> destinationPhotoUrls)
    {
        if (!_config.Enabled)
            return new PhotoCompareReport();

        if (string.IsNullOrWhiteSpace(sourcePersonId))
            throw new ArgumentException("Source person id is required.", nameof(sourcePersonId));

        if (string.IsNullOrWhiteSpace(destinationPersonId))
            throw new ArgumentException("Destination person id is required.", nameof(destinationPersonId));

        if (sourcePhotoUrls is null)
            throw new ArgumentNullException(nameof(sourcePhotoUrls));

        if (destinationPhotoUrls is null)
            throw new ArgumentNullException(nameof(destinationPhotoUrls));

        var sourceEntries = await DownloadEntriesAsync(sourcePersonId, sourcePhotoUrls).ConfigureAwait(false);
        var destinationEntries = await DownloadEntriesAsync(destinationPersonId, destinationPhotoUrls).ConfigureAwait(false);

        var (sourceSignatures, sourceUnprocessed) = await BuildSignaturesAsync(sourceEntries).ConfigureAwait(false);
        var (destinationSignatures, _) = await BuildSignaturesAsync(destinationEntries).ConfigureAwait(false);

        var newPhotos = new List<PhotoCacheEntry>(sourceUnprocessed);
        var matched = new List<PhotoCompareResult>();
        var similar = new List<PhotoCompareResult>();

        var destinationPool = new List<PhotoSignature>(destinationSignatures);

        foreach (var source in sourceSignatures)
        {
            var exactMatch = FindExactMatch(source, destinationPool);
            if (exactMatch != null)
            {
                matched.Add(CreateResult(source, exactMatch, 1.0, true, "Content hash match"));
                destinationPool.Remove(exactMatch);
                continue;
            }

            var bestSimilar = FindBestPerceptualMatch(source, destinationPool);
            if (bestSimilar != null)
            {
                similar.Add(CreateResult(source, bestSimilar.Value.Signature, bestSimilar.Value.Similarity, false,
                    "Perceptual hash similarity"));
                destinationPool.Remove(bestSimilar.Value.Signature);
                continue;
            }

            newPhotos.Add(source.Entry);
        }

        // Save cache index if any hashes were computed
        await _photoCacheService.SaveIndexAsync().ConfigureAwait(false);

        return new PhotoCompareReport
        {
            NewPhotos = newPhotos,
            MatchedPhotos = matched,
            SimilarPhotos = similar
        };
    }

    private async Task<IReadOnlyList<PhotoCacheEntry>> DownloadEntriesAsync(
        string personId,
        IReadOnlyList<string> urls)
    {
        if (urls.Count == 0)
            return Array.Empty<PhotoCacheEntry>();

        return await _photoCacheService.EnsureDownloadedAsync(personId, urls).ConfigureAwait(false);
    }

    private async Task<(List<PhotoSignature> Signatures, List<PhotoCacheEntry> Missing)> BuildSignaturesAsync(
        IReadOnlyList<PhotoCacheEntry> entries)
    {
        var signatures = new List<PhotoSignature>(entries.Count);
        var missing = new List<PhotoCacheEntry>();

        foreach (var entry in entries)
        {
            var signature = await CreateSignatureAsync(entry).ConfigureAwait(false);
            if (signature != null)
            {
                signatures.Add(signature);
            }
            else
            {
                missing.Add(entry);
            }
        }

        return (signatures, missing);
    }

    private async Task<PhotoSignature?> CreateSignatureAsync(PhotoCacheEntry entry)
    {
        var contentHash = NormalizeContentHash(entry.ContentHash);
        ulong? perceptualHash = TryParsePerceptualHash(entry.PerceptualHash, out var parsedHash)
            ? parsedHash
            : null;

        if (contentHash != null && perceptualHash.HasValue)
            return new PhotoSignature(entry, contentHash, perceptualHash);

        var data = await _photoCacheService.GetPhotoDataByPathAsync(entry.LocalPath).ConfigureAwait(false);
        if (data == null || data.Length == 0)
        {
            _logger.LogWarning("Cached photo not found for URL {Url} at {Path}", entry.Url, entry.LocalPath);
            return contentHash != null || perceptualHash.HasValue
                ? new PhotoSignature(entry, contentHash, perceptualHash)
                : null;
        }

        var originalContentHash = contentHash;
        var originalPerceptualHash = perceptualHash;

        contentHash ??= _photoHashService.ComputeContentHash(data);

        if (!perceptualHash.HasValue)
        {
            try
            {
                perceptualHash = _photoHashService.ComputePerceptualHash(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute perceptual hash for URL {Url}", entry.Url);
            }
        }

        // Save computed hashes to cache if they were computed
        if (originalContentHash == null || originalPerceptualHash == null)
        {
            var perceptualHashStr = perceptualHash.HasValue
                ? $"phash:0x{perceptualHash.Value:x16}"
                : null;

            _photoCacheService.UpdateEntry(entry.Url, contentHash, perceptualHashStr);
        }

        return contentHash != null || perceptualHash.HasValue
            ? new PhotoSignature(entry, contentHash, perceptualHash)
            : null;
    }

    private static PhotoSignature? FindExactMatch(
        PhotoSignature source,
        List<PhotoSignature> candidates)
    {
        if (string.IsNullOrWhiteSpace(source.ContentHash))
            return null;

        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.ContentHash) &&
            string.Equals(source.ContentHash, candidate.ContentHash, StringComparison.OrdinalIgnoreCase));
    }

    private (PhotoSignature Signature, double Similarity)? FindBestPerceptualMatch(
        PhotoSignature source,
        List<PhotoSignature> candidates)
    {
        if (!source.PerceptualHash.HasValue || candidates.Count == 0)
            return null;

        PhotoSignature? best = null;
        double bestScore = double.MinValue;

        foreach (var candidate in candidates)
        {
            if (!candidate.PerceptualHash.HasValue)
                continue;

            var score = _photoHashService.CompareHashes(
                source.PerceptualHash.Value,
                candidate.PerceptualHash.Value);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best == null || bestScore < _similarityThreshold)
            return null;

        return (best, bestScore);
    }

    private static string? NormalizeContentHash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParsePerceptualHash(string? value, out ulong hash)
    {
        hash = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var processed = value.Trim();

        if (processed.StartsWith("phash:", StringComparison.OrdinalIgnoreCase))
            processed = processed.Substring("phash:".Length);

        if (processed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            processed = processed[2..];

        return ulong.TryParse(processed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash);
    }

    private static PhotoCompareResult CreateResult(
        PhotoSignature source,
        PhotoSignature destination,
        double similarity,
        bool isMatch,
        string reason)
    {
        return new PhotoCompareResult
        {
            SourceUrl = source.Entry.Url,
            DestinationUrl = destination.Entry.Url,
            SourceLocalPath = source.Entry.LocalPath,
            DestinationLocalPath = destination.Entry.LocalPath,
            Similarity = similarity,
            IsMatch = isMatch,
            Reason = reason
        };
    }

    private sealed record PhotoSignature(
        PhotoCacheEntry Entry,
        string? ContentHash,
        ulong? PerceptualHash);
}
