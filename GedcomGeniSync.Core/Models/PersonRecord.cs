using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using GedcomGeniSync.ApiClient.Utils;

namespace GedcomGeniSync.Models;

/// <summary>
/// Unified person record for matching between GEDCOM and Geni
/// Immutable record for thread-safety and predictable state management
/// </summary>
public record PersonRecord
{
    /// <summary>
    /// Source-specific ID (GEDCOM: "@I123@", Geni: "6000000012345678901")
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Source of the record
    /// </summary>
    public required PersonSource Source { get; init; }

    #region Names

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? MaidenName { get; init; }
    public string? MiddleName { get; init; }
    public string? Suffix { get; init; } // Jr., Sr., III, etc.
    public string? Nickname { get; init; }

    /// <summary>
    /// All name variants found in source (for fuzzy matching)
    /// </summary>
    public ImmutableList<string> NameVariants { get; init; } = ImmutableList<string>.Empty;

    public string FullName => string.Join(" ",
        new[] { FirstName, MiddleName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    #endregion

    #region Dates

    public DateInfo? BirthDate { get; init; }
    public DateInfo? DeathDate { get; init; }
    public DateInfo? BurialDate { get; init; }

    #endregion

    #region Places

    public string? BirthPlace { get; init; }
    public string? DeathPlace { get; init; }
    public string? BurialPlace { get; init; }

    #endregion

    #region Personal Info

    public Gender Gender { get; init; } = Gender.Unknown;
    public bool? IsLiving { get; init; }
    public string? Occupation { get; init; }

    #endregion

    #region Contact Info

    /// <summary>
    /// Email address from GEDCOM ADDR or EMAIL tags
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Website URL from GEDCOM WWW tag
    /// </summary>
    public string? Website { get; init; }

    #endregion

    #region Residence (RESI)

    /// <summary>
    /// Current residence - city
    /// </summary>
    public string? ResidenceCity { get; init; }

    /// <summary>
    /// Current residence - state/province
    /// </summary>
    public string? ResidenceState { get; init; }

    /// <summary>
    /// Current residence - country
    /// </summary>
    public string? ResidenceCountry { get; init; }

    /// <summary>
    /// Full residence address (if available)
    /// </summary>
    public string? ResidenceAddress { get; init; }

    /// <summary>
    /// Get formatted residence string
    /// </summary>
    public string? FormattedResidence => string.Join(", ",
        new[] { ResidenceCity, ResidenceState, ResidenceCountry }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    #endregion

    #region Media

    /// <summary>
    /// Photo URLs from GEDCOM (FILE tags in OBJE records)
    /// </summary>
    public ImmutableList<string> PhotoUrls { get; init; } = ImmutableList<string>.Empty;

    #endregion

    #region Relations (IDs)

    /// <summary>
    /// Father's ID in same source
    /// </summary>
    public string? FatherId { get; init; }

    /// <summary>
    /// Mother's ID in same source
    /// </summary>
    public string? MotherId { get; init; }

    /// <summary>
    /// Spouse IDs in same source
    /// </summary>
    public ImmutableList<string> SpouseIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Children IDs in same source
    /// </summary>
    public ImmutableList<string> ChildrenIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Sibling IDs in same source
    /// </summary>
    public ImmutableList<string> SiblingIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Family/Union IDs this person belongs to (as child)
    /// </summary>
    public ImmutableList<string> ChildOfFamilyIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Family/Union IDs this person belongs to (as spouse)
    /// </summary>
    public ImmutableList<string> SpouseOfFamilyIds { get; init; } = ImmutableList<string>.Empty;

    #endregion

    #region Matching Helpers

    /// <summary>
    /// Normalized first name for matching (lowercase, transliterated)
    /// </summary>
    public string? NormalizedFirstName { get; init; }

    /// <summary>
    /// Normalized last name for matching
    /// </summary>
    public string? NormalizedLastName { get; init; }

    /// <summary>
    /// Birth year extracted from date (for quick filtering)
    /// </summary>
    public int? BirthYear => BirthDate?.Year;

    /// <summary>
    /// Death year extracted from date
    /// </summary>
    public int? DeathYear => DeathDate?.Year;

    #endregion

    #region Sync State

    /// <summary>
    /// Geni Profile ID extracted from RFN tag (e.g., "geni:6000000012345678901")
    /// Used for matching GEDCOM records to Geni profiles
    /// </summary>
    public string? GeniProfileId { get; init; }

    /// <summary>
    /// Extract numeric Geni ID from either GEDCOM ID or RFN
    /// Returns the numeric part that uniquely identifies a Geni profile
    /// </summary>
    public string? GetNumericGeniId()
    {
        // Try to extract from RFN first (most reliable for Geni-exported files)
        if (!string.IsNullOrEmpty(GeniProfileId))
        {
            var numericFromRfn = GeniIdHelper.ExtractNumericId(GeniProfileId);
            if (numericFromRfn != null)
                return numericFromRfn;
        }

        // Fall back to GEDCOM ID (works if GEDCOM was exported from Geni)
        return GeniIdHelper.ExtractNumericId(Id);
    }

    /// <summary>
    /// Matched ID in other system (GEDCOM ID → Geni ID or vice versa)
    /// </summary>
    public string? MatchedId { get; init; }

    /// <summary>
    /// Match confidence score (0-100)
    /// </summary>
    public int? MatchScore { get; init; }

    /// <summary>
    /// Sync status
    /// </summary>
    public SyncStatus SyncStatus { get; init; } = SyncStatus.Pending;

    /// <summary>
    /// Notes about this person from GEDCOM NOTE tag
    /// Can contain biographical information, research notes, etc.
    /// </summary>
    public ImmutableList<string> Notes { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Custom tags from GEDCOM (e.g., MyHeritage _UPD, _UID, RIN, etc.)
    /// Stored as key-value pairs where key is tag name and value is tag content
    /// For multi-line tags, values are concatenated with newlines
    /// </summary>
    public ImmutableDictionary<string, string> CustomTags { get; init; } = ImmutableDictionary<string, string>.Empty;

    #endregion

    public override string ToString()
    {
        var birth = BirthYear.HasValue ? $" (*{BirthYear})" : "";
        var death = DeathYear.HasValue ? $" (†{DeathYear})" : "";
        return $"{FullName}{birth}{death} [{Source}:{Id}]";
    }
}

/// <summary>
/// Flexible date representation handling GEDCOM date formats
/// Immutable record for thread-safety
/// Uses DateOnly for type-safe date handling
/// </summary>
public record DateInfo
{
    /// <summary>
    /// The actual date value (with day=1 for year-only, day=1 for year+month-only dates)
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Precision of the date (Year, Month, or Day)
    /// </summary>
    public DatePrecision Precision { get; init; } = DatePrecision.Day;

    /// <summary>
    /// Original date string from source
    /// </summary>
    public string? Original { get; init; }

    /// <summary>
    /// Date modifier (About, Before, After, etc.)
    /// </summary>
    public DateModifier Modifier { get; init; } = DateModifier.Exact;

    /// <summary>
    /// For date ranges (BETWEEN x AND y)
    /// </summary>
    public DateInfo? RangeEnd { get; init; }

    public bool HasValue => Date.HasValue;

    /// <summary>
    /// Year component for backward compatibility
    /// </summary>
    public int? Year => Date?.Year;

    /// <summary>
    /// Month component for backward compatibility
    /// </summary>
    public int? Month => Precision >= DatePrecision.Month ? Date?.Month : null;

    /// <summary>
    /// Day component for backward compatibility
    /// </summary>
    public int? Day => Precision >= DatePrecision.Day ? Date?.Day : null;

    /// <summary>
    /// Format for Geni API: "YYYY-MM-DD", "YYYY-MM", or "YYYY"
    /// </summary>
    public string? ToGeniFormat()
    {
        if (!Date.HasValue) return null;

        return Precision switch
        {
            DatePrecision.Year => Date.Value.Year.ToString(),
            DatePrecision.Month => $"{Date.Value.Year:D4}-{Date.Value.Month:D2}",
            DatePrecision.Day => $"{Date.Value.Year:D4}-{Date.Value.Month:D2}-{Date.Value.Day:D2}",
            _ => null
        };
    }

    public override string ToString()
    {
        var prefix = Modifier switch
        {
            DateModifier.About => "ABT ",
            DateModifier.Before => "BEF ",
            DateModifier.After => "AFT ",
            DateModifier.Estimated => "EST ",
            DateModifier.Calculated => "CAL ",
            DateModifier.Between => "BET ",
            _ => ""
        };

        if (!string.IsNullOrEmpty(Original))
            return $"{prefix}{Original}";

        if (!HasValue)
            return string.Empty;

        var date = Precision switch
        {
            DatePrecision.Year => Date!.Value.Year.ToString(),
            DatePrecision.Month => $"{Date!.Value.Month:D2}/{Date!.Value.Year}",
            DatePrecision.Day => $"{Date!.Value.Day:D2}/{Date!.Value.Month:D2}/{Date!.Value.Year}",
            _ => ""
        };

        return $"{prefix}{date}";
    }

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        // English (GEDCOM standard)
        ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4,
        ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AUG"] = 8,
        ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12,
        // English full names
        ["JANUARY"] = 1, ["FEBRUARY"] = 2, ["MARCH"] = 3, ["APRIL"] = 4,
        ["JUNE"] = 6, ["JULY"] = 7, ["AUGUST"] = 8,
        ["SEPTEMBER"] = 9, ["OCTOBER"] = 10, ["NOVEMBER"] = 11, ["DECEMBER"] = 12,
        // Russian (common in Slavic GEDCOM files)
        ["ЯНВ"] = 1, ["ФЕВ"] = 2, ["МАР"] = 3, ["АПР"] = 4,
        ["МАЙ"] = 5, ["ИЮН"] = 6, ["ИЮЛ"] = 7, ["АВГ"] = 8,
        ["СЕН"] = 9, ["ОКТ"] = 10, ["НОЯ"] = 11, ["ДЕК"] = 12,
        // Russian full names
        ["ЯНВАРЬ"] = 1, ["ФЕВРАЛЬ"] = 2, ["МАРТ"] = 3, ["АПРЕЛЬ"] = 4,
        ["МАЙТ"] = 5, ["ИЮНЬ"] = 6, ["ИЮЛЬ"] = 7, ["АВГУСТ"] = 8,
        ["СЕНТЯБРЬ"] = 9, ["ОКТЯБРЬ"] = 10, ["НОЯБРЬ"] = 11, ["ДЕКАБРЬ"] = 12,
        // German
        ["MÄR"] = 3, ["MAI"] = 5, ["OKT"] = 10, ["DEZ"] = 12,
        // French
        ["JANV"] = 1, ["FÉVR"] = 2, ["MARS"] = 3, ["AVR"] = 4,
        ["JUIN"] = 6, ["JUIL"] = 7, ["AOÛT"] = 8,
        ["SEPT"] = 9, ["DÉC"] = 12
    };

    /// <summary>
    /// Parse GEDCOM date string
    /// </summary>
    public static DateInfo? Parse(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;

        var original = dateString;
        var upper = dateString.ToUpperInvariant().Trim();
        var modifier = DateModifier.Exact;
        DateInfo? rangeEnd = null;

        // Handle modifiers
        if (upper.StartsWith("ABT ") || upper.StartsWith("ABOUT "))
        {
            modifier = DateModifier.About;
            upper = TrimPrefix(upper, "ABT ", "ABOUT ");
        }
        else if (upper.StartsWith("BEF ") || upper.StartsWith("BEFORE "))
        {
            modifier = DateModifier.Before;
            upper = TrimPrefix(upper, "BEF ", "BEFORE ");
        }
        else if (upper.StartsWith("AFT ") || upper.StartsWith("AFTER "))
        {
            modifier = DateModifier.After;
            upper = TrimPrefix(upper, "AFT ", "AFTER ");
        }
        else if (upper.StartsWith("EST "))
        {
            modifier = DateModifier.Estimated;
            upper = TrimPrefix(upper, "EST ");
        }
        else if (upper.StartsWith("CAL "))
        {
            modifier = DateModifier.Calculated;
            upper = TrimPrefix(upper, "CAL ");
        }
        else if (upper.StartsWith("BET "))
        {
            modifier = DateModifier.Between;
            var parts = TrimPrefix(upper, "BET ").Split(" AND ");
            if (parts.Length == 2)
            {
                upper = parts[0].Trim();
                rangeEnd = Parse(parts[1].Trim());
            }
        }

        // Parse the actual date
        var (year, month, day) = ParseDatePart(upper);

        if (!year.HasValue)
            return null;

        // Determine precision and create DateOnly
        DatePrecision precision;
        DateOnly date;

        if (day.HasValue && month.HasValue)
        {
            precision = DatePrecision.Day;
            try
            {
                date = new DateOnly(year.Value, month.Value, day.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date (e.g., Feb 31), fall back to month precision
                precision = DatePrecision.Month;
                date = new DateOnly(year.Value, month.Value, 1);
            }
        }
        else if (month.HasValue)
        {
            precision = DatePrecision.Month;
            date = new DateOnly(year.Value, month.Value, 1);
        }
        else
        {
            precision = DatePrecision.Year;
            date = new DateOnly(year.Value, 1, 1);
        }

        return new DateInfo
        {
            Date = date,
            Precision = precision,
            Original = original,
            Modifier = modifier,
            RangeEnd = rangeEnd
        };
    }

    private static string TrimPrefix(string value, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value;
    }

    private static (int? year, int? month, int? day) ParseDatePart(string dateStr)
    {
        // Multi-language month names support (GEDCOM standard + common variations)
        var parts = dateStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int? year = null;
        int? month = null;
        int? day = null;

        foreach (var part in parts)
        {
            // Check if it's a month name
            if (Months.TryGetValue(part, out var monthValue))
            {
                month = monthValue;
            }
            // Check if it's a year (4 digits)
            else if (part.Length == 4 && int.TryParse(part, out var yearValue))
            {
                year = yearValue;
            }
            // Check if it's a day (1-2 digits)
            else if (part.Length <= 2 && int.TryParse(part, out var dayValue) && dayValue >= 1 && dayValue <= 31)
            {
                day = dayValue;
            }
        }

        // If parsing failed with dictionary, try standard date parsing with multiple cultures
        if (!year.HasValue && !month.HasValue && !day.HasValue)
        {
            var cultures = new[]
            {
                CultureInfo.InvariantCulture,
                new CultureInfo("en-US"),
                new CultureInfo("ru-RU"),
                new CultureInfo("de-DE"),
                new CultureInfo("fr-FR")
            };

            foreach (var culture in cultures)
            {
                if (DateTime.TryParse(dateStr, culture, DateTimeStyles.None, out var parsedDate))
                {
                    year = parsedDate.Year;
                    month = parsedDate.Month;
                    day = parsedDate.Day;
                    break;
                }
            }
        }

        return (year, month, day);
    }
}

public enum DatePrecision
{
    Year,   // Only year is known
    Month,  // Year and month are known
    Day     // Full date is known
}

public enum DateModifier
{
    Exact,
    About,      // ABT
    Before,     // BEF
    After,      // AFT
    Estimated,  // EST
    Calculated, // CAL
    Between     // BET ... AND ...
}

public enum Gender
{
    Unknown,
    Male,
    Female
}

public enum PersonSource
{
    Gedcom,
    Geni
}

public enum SyncStatus
{
    Pending,
    Matched,
    Created,
    Skipped,
    Error
}

/// <summary>
/// Result of duplicate/match search
/// Immutable record for thread-safety
/// </summary>
public record MatchCandidate
{
    public required PersonRecord Source { get; init; }
    public required PersonRecord Target { get; init; }
    public double Score { get; init; }
    public ImmutableList<MatchReason> Reasons { get; init; } = ImmutableList<MatchReason>.Empty;

    public override string ToString() =>
        $"{Source.FullName} ↔ {Target.FullName} (Score: {Math.Round(Score)}%)";
}

public record MatchReason
{
    public required string Field { get; init; }
    public double Points { get; init; }
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// Sync operation result for reporting
/// Immutable record for thread-safety
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public record SyncResult
{
    public required string GedcomId { get; init; }
    public string? GeniId { get; init; }
    public required string PersonName { get; init; }
    public SyncAction Action { get; init; }
    public int? MatchScore { get; init; }
    public string? RelationType { get; init; } // "child", "parent", "partner"
    public string? RelativeGeniId { get; init; } // Parent/child to link to
    public string? ErrorMessage { get; init; }
}

public enum SyncAction
{
    Matched,    // Found existing profile in Geni
    Created,    // Created new profile in Geni
    Skipped,    // Skipped (insufficient data)
    Error       // Failed to process
}
