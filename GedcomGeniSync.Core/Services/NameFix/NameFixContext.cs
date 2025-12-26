using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Context object passed through the name fix pipeline.
/// Contains the profile data being processed and tracks all changes.
/// </summary>
public class NameFixContext
{
    /// <summary>
    /// Geni profile ID being processed
    /// </summary>
    public required string ProfileId { get; init; }

    /// <summary>
    /// Profile URL for logging
    /// </summary>
    public string? ProfileUrl { get; init; }

    /// <summary>
    /// Display name for logging
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gender of the person (important for feminine surname handling)
    /// </summary>
    public Gender Gender { get; init; } = Gender.Unknown;

    /// <summary>
    /// Primary first name (from profile root)
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Primary middle name (from profile root)
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Primary last name (from profile root)
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Primary maiden name (from profile root)
    /// </summary>
    public string? MaidenName { get; set; }

    /// <summary>
    /// Suffix (Jr., Sr., III, etc.)
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>
    /// Multilingual names dictionary.
    /// Structure: Names[locale][field] = value
    /// Example: Names["ru"]["first_name"] = "Иван"
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Names { get; set; } = new();

    /// <summary>
    /// List of all changes made during processing
    /// </summary>
    public List<NameChange> Changes { get; } = new();

    /// <summary>
    /// Whether any changes were made
    /// </summary>
    public bool IsDirty => Changes.Count > 0;

    /// <summary>
    /// Original Names dictionary for comparison (set at start of processing)
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? OriginalNames { get; private set; }

    #region Factory Methods

    /// <summary>
    /// Create context from a GeniProfile
    /// </summary>
    public static NameFixContext FromGeniProfile(GeniProfile profile)
    {
        var context = new NameFixContext
        {
            ProfileId = profile.Id,
            ProfileUrl = profile.ProfileUrl ?? profile.Url,
            DisplayName = profile.DisplayName ?? profile.Name,
            Gender = ParseGender(profile.Gender),
            FirstName = profile.FirstName,
            MiddleName = profile.MiddleName,
            LastName = profile.LastName,
            MaidenName = profile.MaidenName,
            Suffix = profile.Suffix,
            Names = CloneNames(profile.Names)
        };

        // Store original for comparison
        context.OriginalNames = CloneNames(profile.Names);

        return context;
    }

    /// <summary>
    /// Create context from a GeniNode (immediate-family response)
    /// </summary>
    public static NameFixContext FromGeniNode(GeniNode node)
    {
        var context = new NameFixContext
        {
            ProfileId = node.Id ?? string.Empty,
            DisplayName = node.Name,
            Gender = ParseGender(node.Gender),
            FirstName = node.FirstName,
            MiddleName = node.MiddleName,
            LastName = node.LastName,
            MaidenName = node.MaidenName,
            Suffix = node.Suffix,
            Names = CloneNames(node.Names)
        };

        // Store original for comparison
        context.OriginalNames = CloneNames(node.Names);

        return context;
    }

    #endregion

    #region Name Access Methods

    /// <summary>
    /// Get a name field from a specific locale
    /// </summary>
    public string? GetName(string locale, string field)
    {
        if (Names.TryGetValue(locale, out var fields) &&
            fields.TryGetValue(field, out var value))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    /// <summary>
    /// Set a name field in a specific locale and record the change
    /// </summary>
    public void SetName(string locale, string field, string? value, string reason, string? handler = null)
    {
        var oldValue = GetName(locale, field);

        // Don't record if no actual change
        if (oldValue == value) return;
        if (string.IsNullOrWhiteSpace(oldValue) && string.IsNullOrWhiteSpace(value)) return;

        // Ensure locale exists
        if (!Names.ContainsKey(locale))
        {
            Names[locale] = new Dictionary<string, string>();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            // Remove field if value is empty
            Names[locale].Remove(field);
        }
        else
        {
            Names[locale][field] = value;
        }

        // Record the change
        Changes.Add(new NameChange
        {
            Field = field,
            FromLocale = oldValue != null ? locale : null,
            ToLocale = !string.IsNullOrWhiteSpace(value) ? locale : null,
            OldValue = oldValue,
            NewValue = value,
            Reason = reason,
            Handler = handler
        });
    }

    /// <summary>
    /// Move a name field from one locale to another
    /// </summary>
    public void MoveName(string fromLocale, string toLocale, string field, string reason, string? handler = null)
    {
        var value = GetName(fromLocale, field);
        if (string.IsNullOrWhiteSpace(value)) return;

        // Remove from source
        if (Names.TryGetValue(fromLocale, out var fromFields))
        {
            fromFields.Remove(field);
        }

        // Add to target
        if (!Names.ContainsKey(toLocale))
        {
            Names[toLocale] = new Dictionary<string, string>();
        }
        Names[toLocale][field] = value;

        // Record the change
        Changes.Add(new NameChange
        {
            Field = field,
            FromLocale = fromLocale,
            ToLocale = toLocale,
            OldValue = value,
            NewValue = value,
            Reason = reason,
            Handler = handler
        });
    }

    /// <summary>
    /// Check if a locale has any non-empty fields
    /// </summary>
    public bool HasLocale(string locale)
    {
        if (!Names.TryGetValue(locale, out var fields)) return false;
        return fields.Values.Any(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>
    /// Get all locales that have data
    /// </summary>
    public IEnumerable<string> GetActiveLocales()
    {
        return Names
            .Where(kvp => kvp.Value.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Get all fields for a locale
    /// </summary>
    public IReadOnlyDictionary<string, string>? GetLocaleFields(string locale)
    {
        return Names.TryGetValue(locale, out var fields) ? fields : null;
    }

    #endregion

    #region Conversion

    /// <summary>
    /// Convert context back to GeniProfileUpdate for API call
    /// </summary>
    public GeniProfileUpdate ToProfileUpdate()
    {
        return new GeniProfileUpdate
        {
            FirstName = FirstName,
            MiddleName = MiddleName,
            LastName = LastName,
            MaidenName = MaidenName,
            Suffix = Suffix,
            Names = Names.Count > 0 ? Names : null
        };
    }

    #endregion

    #region Helpers

    private static Gender ParseGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return Gender.Unknown;

        return gender.ToLowerInvariant() switch
        {
            "male" => Gender.Male,
            "female" => Gender.Female,
            "m" => Gender.Male,
            "f" => Gender.Female,
            _ => Gender.Unknown
        };
    }

    private static Dictionary<string, Dictionary<string, string>> CloneNames(
        Dictionary<string, Dictionary<string, string>>? names)
    {
        if (names == null) return new Dictionary<string, Dictionary<string, string>>();

        return names.ToDictionary(
            kvp => kvp.Key,
            kvp => new Dictionary<string, string>(kvp.Value)
        );
    }

    #endregion
}

/// <summary>
/// Standard name field names used in Geni API
/// </summary>
public static class NameFields
{
    public const string FirstName = "first_name";
    public const string MiddleName = "middle_name";
    public const string LastName = "last_name";
    public const string MaidenName = "maiden_name";
    public const string Suffix = "suffix";
    public const string Title = "title";

    public static readonly string[] All = { FirstName, MiddleName, LastName, MaidenName, Suffix, Title };

    /// <summary>
    /// Name fields that can contain surnames (for feminine suffix handling)
    /// </summary>
    public static readonly string[] SurnameFields = { LastName, MaidenName };
}

/// <summary>
/// Common locale codes used in Geni
/// </summary>
public static class Locales
{
    public const string English = "en-US";
    public const string EnglishShort = "en";
    public const string Russian = "ru";
    public const string Lithuanian = "lt";
    public const string Estonian = "et";
    public const string German = "de";
    public const string Hebrew = "he";
    public const string Ukrainian = "uk";
    public const string Polish = "pl";
    public const string Latvian = "lv";

    /// <summary>
    /// English locale variants (both en and en-US should be treated as English)
    /// </summary>
    public static bool IsEnglish(string locale) =>
        locale.Equals(English, StringComparison.OrdinalIgnoreCase) ||
        locale.Equals(EnglishShort, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get the preferred English locale for writing
    /// </summary>
    public static string PreferredEnglish => English;
}
