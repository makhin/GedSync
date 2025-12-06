using System.Diagnostics.CodeAnalysis;

namespace GedcomGeniSync.Services;

/// <summary>
/// Synchronization options. Immutable record for thread-safety.
/// </summary>
[ExcludeFromCodeCoverage]
public record SyncOptions
{
    public string? StateFilePath { get; init; }
    public int? MaxDepth { get; init; }
    public MatchingOptions MatchingOptions { get; init; } = new();

    /// <summary>
    /// Enable photo synchronization from GEDCOM to Geni.
    /// </summary>
    public bool SyncPhotos { get; init; } = true;
}
