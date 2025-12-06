using System.Collections.Immutable;

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
/// </summary>
public record DateInfo
{
    public int? Year { get; init; }
    public int? Month { get; init; }
    public int? Day { get; init; }

    /// <summary>
    /// Original date string from source
    /// </summary>
    public string? Original { get; init; }

    /// <summary>
    /// Date precision/modifier
    /// </summary>
    public DateModifier Modifier { get; init; } = DateModifier.Exact;

    /// <summary>
    /// For date ranges (BETWEEN x AND y)
    /// </summary>
    public DateInfo? RangeEnd { get; init; }

    public bool HasValue => Year.HasValue || Month.HasValue || Day.HasValue;

    /// <summary>
    /// Format for Geni API: "YYYY-MM-DD", "YYYY-MM", or "YYYY"
    /// </summary>
    public string? ToGeniFormat()
    {
        if (!Year.HasValue) return null;

        if (Day.HasValue && Month.HasValue)
            return $"{Year:D4}-{Month:D2}-{Day:D2}";

        if (Month.HasValue)
            return $"{Year:D4}-{Month:D2}";

        return Year.ToString();
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

        var date = Year.HasValue ? Year.ToString() : "";
        if (Month.HasValue) date = $"{Month:D2}/{date}";
        if (Day.HasValue) date = $"{Day:D2}/{date}";

        return $"{prefix}{date}";
    }

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
            upper = upper.Replace("ABT ", "").Replace("ABOUT ", "").Trim();
        }
        else if (upper.StartsWith("BEF ") || upper.StartsWith("BEFORE "))
        {
            modifier = DateModifier.Before;
            upper = upper.Replace("BEF ", "").Replace("BEFORE ", "").Trim();
        }
        else if (upper.StartsWith("AFT ") || upper.StartsWith("AFTER "))
        {
            modifier = DateModifier.After;
            upper = upper.Replace("AFT ", "").Replace("AFTER ", "").Trim();
        }
        else if (upper.StartsWith("EST "))
        {
            modifier = DateModifier.Estimated;
            upper = upper.Replace("EST ", "").Trim();
        }
        else if (upper.StartsWith("CAL "))
        {
            modifier = DateModifier.Calculated;
            upper = upper.Replace("CAL ", "").Trim();
        }
        else if (upper.StartsWith("BET "))
        {
            modifier = DateModifier.Between;
            var parts = upper.Replace("BET ", "").Split(" AND ");
            if (parts.Length == 2)
            {
                upper = parts[0].Trim();
                rangeEnd = Parse(parts[1].Trim());
            }
        }

        // Parse the actual date
        var (year, month, day) = ParseDatePart(upper);

        if (!year.HasValue && !month.HasValue && !day.HasValue)
            return null;

        return new DateInfo
        {
            Year = year,
            Month = month,
            Day = day,
            Original = original,
            Modifier = modifier,
            RangeEnd = rangeEnd
        };
    }

    private static (int? year, int? month, int? day) ParseDatePart(string dateStr)
    {
        // Common GEDCOM month names
        var months = new Dictionary<string, int>
        {
            ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4,
            ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AUG"] = 8,
            ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12
        };

        var parts = dateStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int? year = null;
        int? month = null;
        int? day = null;

        foreach (var part in parts)
        {
            // Check if it's a month name
            if (months.TryGetValue(part.ToUpperInvariant(), out var monthValue))
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

        return (year, month, day);
    }
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
    public int Score { get; init; }
    public ImmutableList<MatchReason> Reasons { get; init; } = ImmutableList<MatchReason>.Empty;

    public override string ToString() =>
        $"{Source.FullName} ↔ {Target.FullName} (Score: {Score}%)";
}

public record MatchReason
{
    public required string Field { get; init; }
    public int Points { get; init; }
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// Sync operation result for reporting
/// Immutable record for thread-safety
/// </summary>
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
