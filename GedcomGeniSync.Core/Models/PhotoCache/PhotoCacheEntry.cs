namespace GedcomGeniSync.Models;

public record PhotoCacheEntry
{
    public required string Url { get; init; }
    public required string LocalPath { get; init; }
    public required string PersonId { get; init; }
    public required string Source { get; init; }
    public long FileSize { get; init; }
    public string? ContentHash { get; init; }
    public string? PerceptualHash { get; init; }
    public DateTime DownloadedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }

    /// <summary>
    /// URL of the photo this entry was matched with (from another source).
    /// When set, indicates this photo has already been compared and matched.
    /// </summary>
    public string? MatchedWithUrl { get; init; }

    /// <summary>
    /// When the match was recorded.
    /// </summary>
    public DateTime? MatchedAt { get; init; }
}
