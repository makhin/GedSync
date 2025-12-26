using System.Text.Json.Serialization;
using GedcomGeniSync.Services.NameFix;

namespace GedcomGeniSync.Cli.Models;

/// <summary>
/// Progress state for fix-names command, supporting resume after interruption
/// </summary>
public class FixNamesProgress
{
    /// <summary>
    /// Timestamp when processing started
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of last update
    /// </summary>
    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Anchor profile ID that started the BFS
    /// </summary>
    [JsonPropertyName("anchor_profile")]
    public string? AnchorProfile { get; set; }

    /// <summary>
    /// Maximum depth for BFS traversal
    /// </summary>
    [JsonPropertyName("max_depth")]
    public int? MaxDepth { get; set; }

    /// <summary>
    /// Set of profile IDs that have been processed
    /// </summary>
    [JsonPropertyName("processed_profiles")]
    public HashSet<string> ProcessedProfiles { get; set; } = new();

    /// <summary>
    /// Set of profile IDs whose names have been processed
    /// </summary>
    [JsonPropertyName("name_processed_profiles")]
    public HashSet<string> NameProcessedProfiles { get; set; } = new();

    /// <summary>
    /// Profiles that had changes applied
    /// </summary>
    [JsonPropertyName("changed_profiles")]
    public HashSet<string> ChangedProfiles { get; set; } = new();

    /// <summary>
    /// Profiles that failed to process
    /// </summary>
    [JsonPropertyName("failed_profiles")]
    public HashSet<string> FailedProfiles { get; set; } = new();

    /// <summary>
    /// BFS queue state for resumption (profile ID -> depth)
    /// </summary>
    [JsonPropertyName("queue_state")]
    public List<QueueEntry> QueueState { get; set; } = new();

    /// <summary>
    /// Total number of changes made
    /// </summary>
    [JsonPropertyName("total_changes")]
    public int TotalChanges { get; set; }

    /// <summary>
    /// Detailed change log (last N entries)
    /// </summary>
    [JsonPropertyName("recent_changes")]
    public List<ProfileChangeLog> RecentChanges { get; set; } = new();

    /// <summary>
    /// Maximum number of recent changes to keep in memory
    /// </summary>
    [JsonIgnore]
    public int MaxRecentChanges { get; set; } = 1000;

    /// <summary>
    /// Add a profile change log entry
    /// </summary>
    public void AddChange(ProfileChangeLog change)
    {
        RecentChanges.Add(change);
        TotalChanges += change.Changes.Count;

        // Trim if too many
        if (RecentChanges.Count > MaxRecentChanges)
        {
            RecentChanges.RemoveRange(0, RecentChanges.Count - MaxRecentChanges);
        }
    }

    /// <summary>
    /// Mark profile as expanded in BFS
    /// </summary>
    public void MarkExpanded(string profileId)
    {
        ProcessedProfiles.Add(profileId);
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark profile names as processed
    /// </summary>
    public void MarkNameProcessed(string profileId, bool hadChanges)
    {
        NameProcessedProfiles.Add(profileId);

        if (hadChanges)
        {
            ChangedProfiles.Add(profileId);
        }

        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark profile as failed
    /// </summary>
    public void MarkFailed(string profileId)
    {
        ProcessedProfiles.Add(profileId);
        FailedProfiles.Add(profileId);
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Check if profile was already expanded
    /// </summary>
    public bool IsExpanded(string profileId) => ProcessedProfiles.Contains(profileId);

    /// <summary>
    /// Check if profile names were already processed
    /// </summary>
    public bool IsNameProcessed(string profileId) => NameProcessedProfiles.Contains(profileId);
}

/// <summary>
/// Entry in the BFS queue for serialization
/// </summary>
public class QueueEntry
{
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }
}

/// <summary>
/// Log entry for a single profile's changes
/// </summary>
public class ProfileChangeLog
{
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("profile_url")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("changes")]
    public List<NameChangeLog> Changes { get; set; } = new();

    public static ProfileChangeLog FromContext(NameFixContext context)
    {
        return new ProfileChangeLog
        {
            ProfileId = context.ProfileId,
            ProfileUrl = context.ProfileUrl,
            DisplayName = context.DisplayName,
            Changes = context.Changes.Select(c => new NameChangeLog
            {
                Field = c.Field,
                FromLocale = c.FromLocale,
                ToLocale = c.ToLocale,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                Reason = c.Reason,
                Handler = c.Handler
            }).ToList()
        };
    }
}

/// <summary>
/// Serializable version of NameChange
/// </summary>
public class NameChangeLog
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("from_locale")]
    public string? FromLocale { get; set; }

    [JsonPropertyName("to_locale")]
    public string? ToLocale { get; set; }

    [JsonPropertyName("old_value")]
    public string? OldValue { get; set; }

    [JsonPropertyName("new_value")]
    public string? NewValue { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("handler")]
    public string? Handler { get; set; }
}
