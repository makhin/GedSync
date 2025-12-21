namespace GedcomGeniSync.Models;

public record PhotoCompareReport
{
    public IReadOnlyList<PhotoCacheEntry> NewPhotos { get; init; } = Array.Empty<PhotoCacheEntry>();

    public IReadOnlyList<PhotoCompareResult> MatchedPhotos { get; init; } = Array.Empty<PhotoCompareResult>();

    public IReadOnlyList<PhotoCompareResult> SimilarPhotos { get; init; } = Array.Empty<PhotoCompareResult>();
}
