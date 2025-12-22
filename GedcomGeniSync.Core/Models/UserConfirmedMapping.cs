using System.Text.Json.Serialization;

namespace GedcomGeniSync.Core.Models;

/// <summary>
/// Represents a user-confirmed or rejected mapping between source and destination individuals
/// </summary>
public class UserConfirmedMapping
{
    [JsonPropertyName("sourceId")]
    public required string SourceId { get; set; }

    [JsonPropertyName("destinationId")]
    public string? DestinationId { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConfirmationType Type { get; set; }

    [JsonPropertyName("confirmedAt")]
    public DateTime ConfirmedAt { get; set; }

    [JsonPropertyName("originalScore")]
    public int OriginalScore { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

/// <summary>
/// Type of user confirmation for a mapping
/// </summary>
public enum ConfirmationType
{
    /// <summary>
    /// User confirmed this mapping is correct
    /// </summary>
    Confirmed,

    /// <summary>
    /// User rejected this mapping (not a match)
    /// </summary>
    Rejected,

    /// <summary>
    /// User skipped decision (to be decided later)
    /// </summary>
    Skipped
}

/// <summary>
/// Container for all confirmed mappings
/// </summary>
public class ConfirmedMappingsFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("destinationFile")]
    public string? DestinationFile { get; set; }

    [JsonPropertyName("mappings")]
    public List<UserConfirmedMapping> Mappings { get; set; } = new();
}
