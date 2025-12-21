using System.Diagnostics.CodeAnalysis;
using GedcomGeniSync.Services;
using System.Text.Json.Serialization;

namespace GedcomGeniSync.Models;

/// <summary>
/// Application configuration
/// </summary>
[ExcludeFromCodeCoverage]
public class GedSyncConfiguration
{
    /// <summary>
    /// Matching algorithm options
    /// </summary>
    [JsonPropertyName("matching")]
    public MatchingConfig Matching { get; set; } = new();

    /// <summary>
    /// Synchronization options
    /// </summary>
    [JsonPropertyName("sync")]
    public SyncConfig Sync { get; set; } = new();

    /// <summary>
    /// Name variants options
    /// </summary>
    [JsonPropertyName("nameVariants")]
    public NameVariantsConfig NameVariants { get; set; } = new();

    /// <summary>
    /// Paths configuration
    /// </summary>
    [JsonPropertyName("paths")]
    public PathsConfig Paths { get; set; } = new();

    /// <summary>
    /// Logging configuration
    /// </summary>
    [JsonPropertyName("logging")]
    public LoggingConfig Logging { get; set; } = new();

    /// <summary>
    /// Compare configuration
    /// </summary>
    [JsonPropertyName("compare")]
    public CompareConfig Compare { get; set; } = new();

    /// <summary>
    /// Photo configuration
    /// </summary>
    [JsonPropertyName("photo")]
    public PhotoConfig Photo { get; set; } = new();
}

/// <summary>
/// Matching algorithm configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class MatchingConfig
{
    /// <summary>
    /// Weight for first name comparison (default: 30)
    /// </summary>
    [JsonPropertyName("firstNameWeight")]
    public int FirstNameWeight { get; set; } = 30;

    /// <summary>
    /// Weight for last name comparison (default: 25)
    /// </summary>
    [JsonPropertyName("lastNameWeight")]
    public int LastNameWeight { get; set; } = 25;

    /// <summary>
    /// Weight for birth date comparison (default: 20)
    /// </summary>
    [JsonPropertyName("birthDateWeight")]
    public int BirthDateWeight { get; set; } = 20;

    /// <summary>
    /// Weight for birth place comparison (default: 15)
    /// </summary>
    [JsonPropertyName("birthPlaceWeight")]
    public int BirthPlaceWeight { get; set; } = 15;

    /// <summary>
    /// Weight for death date comparison (default: 5)
    /// </summary>
    [JsonPropertyName("deathDateWeight")]
    public int DeathDateWeight { get; set; } = 5;

    /// <summary>
    /// Weight for gender comparison (default: 5)
    /// </summary>
    [JsonPropertyName("genderWeight")]
    public int GenderWeight { get; set; } = 5;

    /// <summary>
    /// Weight for family relations comparison (default: 0)
    /// Family relations include parents, spouses, children, and siblings
    /// Higher weight increases accuracy but requires existing family data in Geni
    /// </summary>
    [JsonPropertyName("familyRelationsWeight")]
    public int FamilyRelationsWeight { get; set; } = 0;

    /// <summary>
    /// Minimum match score threshold (0-100, default: 70)
    /// </summary>
    [JsonPropertyName("matchThreshold")]
    public int MatchThreshold { get; set; } = 70;

    /// <summary>
    /// Automatic match threshold - matches above this are considered certain (default: 90)
    /// </summary>
    [JsonPropertyName("autoMatchThreshold")]
    public int AutoMatchThreshold { get; set; } = 90;

    /// <summary>
    /// Maximum allowed birth year difference (default: 10)
    /// </summary>
    [JsonPropertyName("maxBirthYearDifference")]
    public int MaxBirthYearDifference { get; set; } = 10;

    /// <summary>
    /// Convert to MatchingOptions
    /// </summary>
    public MatchingOptions ToMatchingOptions()
    {
        return new MatchingOptions
        {
            FirstNameWeight = FirstNameWeight,
            LastNameWeight = LastNameWeight,
            BirthDateWeight = BirthDateWeight,
            BirthPlaceWeight = BirthPlaceWeight,
            DeathDateWeight = DeathDateWeight,
            GenderWeight = GenderWeight,
            FamilyRelationsWeight = FamilyRelationsWeight,
            MatchThreshold = MatchThreshold,
            AutoMatchThreshold = AutoMatchThreshold,
            MaxBirthYearDifference = MaxBirthYearDifference
        };
    }
}

/// <summary>
/// Synchronization configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class SyncConfig
{
    /// <summary>
    /// Maximum BFS depth (null for unlimited)
    /// </summary>
    [JsonPropertyName("maxDepth")]
    public int? MaxDepth { get; set; }

    /// <summary>
    /// Enable dry-run mode (preview changes without creating profiles)
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Enable photo synchronization from GEDCOM to Geni
    /// Downloads photos from MyHeritage URLs found in GEDCOM and uploads to Geni profiles
    /// </summary>
    [JsonPropertyName("syncPhotos")]
    public bool SyncPhotos { get; set; } = true;
}

/// <summary>
/// Name variants configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class NameVariantsConfig
{
    /// <summary>
    /// Path to given names CSV file
    /// </summary>
    [JsonPropertyName("givenNamesCsv")]
    public string? GivenNamesCsv { get; set; }

    /// <summary>
    /// Path to surnames CSV file
    /// </summary>
    [JsonPropertyName("surnamesCsv")]
    public string? SurnamesCsv { get; set; }
}

/// <summary>
/// Paths configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class PathsConfig
{
    /// <summary>
    /// Path to state file for resume support
    /// </summary>
    [JsonPropertyName("stateFile")]
    public string StateFile { get; set; } = "sync_state.json";

    /// <summary>
    /// Path to save sync report
    /// </summary>
    [JsonPropertyName("reportFile")]
    public string ReportFile { get; set; } = "sync_report.json";
}

/// <summary>
/// Logging configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class LoggingConfig
{
    /// <summary>
    /// Enable verbose logging
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;
}

/// <summary>
/// Compare configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class CompareConfig
{
    /// <summary>
    /// Depth of new nodes to add from existing matched nodes (default: 1)
    /// </summary>
    [JsonPropertyName("newNodeDepth")]
    public int NewNodeDepth { get; set; } = 1;

    /// <summary>
    /// Match threshold score 0-100 (default: 70)
    /// </summary>
    [JsonPropertyName("matchThreshold")]
    public int MatchThreshold { get; set; } = 70;

    /// <summary>
    /// Whether to include delete suggestions (default: false)
    /// </summary>
    [JsonPropertyName("includeDeleteSuggestions")]
    public bool IncludeDeleteSuggestions { get; set; } = false;

    /// <summary>
    /// Whether to require unique matches (default: true)
    /// </summary>
    [JsonPropertyName("requireUniqueMatch")]
    public bool RequireUniqueMatch { get; set; } = true;

    /// <summary>
    /// Convert to CompareOptions
    /// </summary>
    public CompareOptions ToCompareOptions(string anchorSourceId, string anchorDestinationId)
    {
        return new CompareOptions
        {
            AnchorSourceId = anchorSourceId,
            AnchorDestinationId = anchorDestinationId,
            NewNodeDepth = NewNodeDepth,
            MatchThreshold = MatchThreshold,
            IncludeDeleteSuggestions = IncludeDeleteSuggestions,
            RequireUniqueMatch = RequireUniqueMatch
        };
    }
}

/// <summary>
/// Photo configuration
/// </summary>
[
    System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage
]
public class PhotoConfig
{
    /// <summary>
    /// Enable photo processing features
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Directory for cached photos
    /// </summary>
    [JsonPropertyName("cacheDirectory")]
    public string CacheDirectory { get; set; } = "./photos";

    /// <summary>
    /// Download photos during GEDCOM load
    /// </summary>
    [JsonPropertyName("downloadOnLoad")]
    public bool DownloadOnLoad { get; set; } = true;

    /// <summary>
    /// Similarity threshold for perceptual hash comparison
    /// </summary>
    [JsonPropertyName("similarityThreshold")]
    public double SimilarityThreshold { get; set; } = 0.95;

    /// <summary>
    /// Maximum concurrent photo downloads
    /// </summary>
    [JsonPropertyName("maxConcurrentDownloads")]
    public int MaxConcurrentDownloads { get; set; } = 4;
}
