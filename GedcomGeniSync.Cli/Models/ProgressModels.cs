namespace GedcomGeniSync.Cli.Models;

/// <summary>
/// Progress tracking for UPDATE command
/// </summary>
public record UpdateProgress
{
    /// <summary>
    /// Path to the original input JSON file
    /// </summary>
    public required string InputFile { get; init; }

    /// <summary>
    /// Path to the GEDCOM file
    /// </summary>
    public required string GedcomFile { get; init; }

    /// <summary>
    /// Timestamp when progress was saved
    /// </summary>
    public DateTime SavedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// List of source IDs that have been successfully processed
    /// </summary>
    public HashSet<string> ProcessedSourceIds { get; init; } = new();

    /// <summary>
    /// Total number of profiles to update
    /// </summary>
    public int TotalProfiles { get; init; }

    /// <summary>
    /// Number of profiles successfully updated
    /// </summary>
    public int UpdatedProfiles { get; init; }

    /// <summary>
    /// Number of profiles that failed
    /// </summary>
    public int FailedProfiles { get; init; }
}

/// <summary>
/// Progress tracking for ADD command
/// </summary>
public record AddProgress
{
    /// <summary>
    /// Path to the original input JSON file
    /// </summary>
    public required string InputFile { get; init; }

    /// <summary>
    /// Path to the GEDCOM file
    /// </summary>
    public required string GedcomFile { get; init; }

    /// <summary>
    /// Timestamp when progress was saved
    /// </summary>
    public DateTime SavedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// List of source IDs that have been successfully processed
    /// </summary>
    public HashSet<string> ProcessedSourceIds { get; init; } = new();

    /// <summary>
    /// Mapping of source IDs to created Geni profile IDs
    /// </summary>
    public Dictionary<string, string> CreatedProfiles { get; init; } = new();

    /// <summary>
    /// Total number of profiles to add
    /// </summary>
    public int TotalProfiles { get; init; }

    /// <summary>
    /// Number of profiles successfully added
    /// </summary>
    public int AddedProfiles { get; init; }

    /// <summary>
    /// Number of profiles that failed
    /// </summary>
    public int FailedProfiles { get; init; }
}
