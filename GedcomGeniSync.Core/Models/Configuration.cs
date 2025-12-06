using GedcomGeniSync.Services;
using System.Text.Json.Serialization;

namespace GedcomGeniSync.Models;

/// <summary>
/// Application configuration
/// </summary>
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
}

/// <summary>
/// Matching algorithm configuration
/// </summary>
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
            MatchThreshold = MatchThreshold,
            AutoMatchThreshold = AutoMatchThreshold,
            MaxBirthYearDifference = MaxBirthYearDifference
        };
    }
}

/// <summary>
/// Synchronization configuration
/// </summary>
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
}

/// <summary>
/// Name variants configuration
/// </summary>
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
public class LoggingConfig
{
    /// <summary>
    /// Enable verbose logging
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;
}
