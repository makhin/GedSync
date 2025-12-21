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
}
