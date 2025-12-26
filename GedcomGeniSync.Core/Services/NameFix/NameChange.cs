namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Represents a single change made to a name field during fix-names processing.
/// Used for logging and auditing all modifications.
/// </summary>
public record NameChange
{
    /// <summary>
    /// The field that was changed: first_name, middle_name, last_name, maiden_name, suffix, title
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Source locale (null if created new, e.g., "en-US")
    /// </summary>
    public string? FromLocale { get; init; }

    /// <summary>
    /// Target locale (null if deleted, e.g., "ru")
    /// </summary>
    public string? ToLocale { get; init; }

    /// <summary>
    /// Original value before change
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// New value after change
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Human-readable reason for the change
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Handler that made this change
    /// </summary>
    public string? Handler { get; init; }

    /// <summary>
    /// If true, this is a warning/suggestion, not an actual change.
    /// Used for typo detection where we suggest but don't auto-fix.
    /// </summary>
    public bool IsWarning { get; init; }

    public override string ToString()
    {
        var from = FromLocale != null ? $"[{FromLocale}]" : "";
        var to = ToLocale != null ? $"[{ToLocale}]" : "";
        return $"{Field}{from} '{OldValue ?? "(null)"}' -> {Field}{to} '{NewValue ?? "(null)"}': {Reason}";
    }
}

/// <summary>
/// Types of changes that can be made
/// </summary>
public enum NameChangeType
{
    /// <summary>Value was moved from one locale to another</summary>
    Moved,

    /// <summary>Value was created (transliteration, etc.)</summary>
    Created,

    /// <summary>Value was modified in place</summary>
    Modified,

    /// <summary>Value was deleted</summary>
    Deleted,

    /// <summary>Value was split into multiple locales</summary>
    Split
}
