namespace GedcomGeniSync.Models;

/// <summary>
/// Unified person record for matching between GEDCOM and Geni
/// </summary>
public class PersonRecord
{
    /// <summary>
    /// Source-specific ID (GEDCOM: "@I123@", Geni: "6000000012345678901")
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Source of the record
    /// </summary>
    public PersonSource Source { get; set; }
    
    #region Names
    
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? MiddleName { get; set; }
    public string? Suffix { get; set; } // Jr., Sr., III, etc.
    public string? Nickname { get; set; }
    
    /// <summary>
    /// All name variants found in source (for fuzzy matching)
    /// </summary>
    public List<string> NameVariants { get; set; } = new();
    
    public string FullName => string.Join(" ", 
        new[] { FirstName, MiddleName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
    
    #endregion
    
    #region Dates
    
    public DateInfo? BirthDate { get; set; }
    public DateInfo? DeathDate { get; set; }
    public DateInfo? BurialDate { get; set; }
    
    #endregion
    
    #region Places
    
    public string? BirthPlace { get; set; }
    public string? DeathPlace { get; set; }
    public string? BurialPlace { get; set; }
    
    #endregion
    
    #region Personal Info
    
    public Gender Gender { get; set; } = Gender.Unknown;
    public bool? IsLiving { get; set; }
    public string? Occupation { get; set; }
    
    #endregion
    
    #region Relations (IDs)
    
    /// <summary>
    /// Father's ID in same source
    /// </summary>
    public string? FatherId { get; set; }
    
    /// <summary>
    /// Mother's ID in same source
    /// </summary>
    public string? MotherId { get; set; }
    
    /// <summary>
    /// Spouse IDs in same source
    /// </summary>
    public List<string> SpouseIds { get; set; } = new();
    
    /// <summary>
    /// Children IDs in same source
    /// </summary>
    public List<string> ChildrenIds { get; set; } = new();
    
    /// <summary>
    /// Sibling IDs in same source
    /// </summary>
    public List<string> SiblingIds { get; set; } = new();
    
    /// <summary>
    /// Family/Union IDs this person belongs to (as child)
    /// </summary>
    public List<string> ChildOfFamilyIds { get; set; } = new();
    
    /// <summary>
    /// Family/Union IDs this person belongs to (as spouse)
    /// </summary>
    public List<string> SpouseOfFamilyIds { get; set; } = new();
    
    #endregion
    
    #region Matching Helpers
    
    /// <summary>
    /// Normalized first name for matching (lowercase, transliterated)
    /// </summary>
    public string? NormalizedFirstName { get; set; }
    
    /// <summary>
    /// Normalized last name for matching
    /// </summary>
    public string? NormalizedLastName { get; set; }
    
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
    public string? MatchedId { get; set; }
    
    /// <summary>
    /// Match confidence score (0-100)
    /// </summary>
    public int? MatchScore { get; set; }
    
    /// <summary>
    /// Sync status
    /// </summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    
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
/// </summary>
public class DateInfo
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    
    /// <summary>
    /// Original date string from source
    /// </summary>
    public string? Original { get; set; }
    
    /// <summary>
    /// Date precision/modifier
    /// </summary>
    public DateModifier Modifier { get; set; } = DateModifier.Exact;
    
    /// <summary>
    /// For date ranges (BETWEEN x AND y)
    /// </summary>
    public DateInfo? RangeEnd { get; set; }
    
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
        
        var result = new DateInfo { Original = dateString };
        var upper = dateString.ToUpperInvariant().Trim();
        
        // Handle modifiers
        if (upper.StartsWith("ABT ") || upper.StartsWith("ABOUT "))
        {
            result.Modifier = DateModifier.About;
            upper = upper.Replace("ABT ", "").Replace("ABOUT ", "").Trim();
        }
        else if (upper.StartsWith("BEF ") || upper.StartsWith("BEFORE "))
        {
            result.Modifier = DateModifier.Before;
            upper = upper.Replace("BEF ", "").Replace("BEFORE ", "").Trim();
        }
        else if (upper.StartsWith("AFT ") || upper.StartsWith("AFTER "))
        {
            result.Modifier = DateModifier.After;
            upper = upper.Replace("AFT ", "").Replace("AFTER ", "").Trim();
        }
        else if (upper.StartsWith("EST "))
        {
            result.Modifier = DateModifier.Estimated;
            upper = upper.Replace("EST ", "").Trim();
        }
        else if (upper.StartsWith("CAL "))
        {
            result.Modifier = DateModifier.Calculated;
            upper = upper.Replace("CAL ", "").Trim();
        }
        else if (upper.StartsWith("BET "))
        {
            result.Modifier = DateModifier.Between;
            var parts = upper.Replace("BET ", "").Split(" AND ");
            if (parts.Length == 2)
            {
                upper = parts[0].Trim();
                result.RangeEnd = Parse(parts[1].Trim());
            }
        }
        
        // Parse the actual date
        ParseDatePart(upper, result);
        
        return result.HasValue ? result : null;
    }
    
    private static void ParseDatePart(string dateStr, DateInfo result)
    {
        // Common GEDCOM month names
        var months = new Dictionary<string, int>
        {
            ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4,
            ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AUG"] = 8,
            ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12
        };
        
        var parts = dateStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            // Check if it's a month name
            if (months.TryGetValue(part.ToUpperInvariant(), out var month))
            {
                result.Month = month;
            }
            // Check if it's a year (4 digits)
            else if (part.Length == 4 && int.TryParse(part, out var year))
            {
                result.Year = year;
            }
            // Check if it's a day (1-2 digits)
            else if (part.Length <= 2 && int.TryParse(part, out var day) && day >= 1 && day <= 31)
            {
                result.Day = day;
            }
        }
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
/// </summary>
public class MatchCandidate
{
    public PersonRecord Source { get; set; } = null!;
    public PersonRecord Target { get; set; } = null!;
    public int Score { get; set; }
    public List<MatchReason> Reasons { get; set; } = new();
    
    public override string ToString() => 
        $"{Source.FullName} ↔ {Target.FullName} (Score: {Score}%)";
}

public class MatchReason
{
    public string Field { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Sync operation result for reporting
/// </summary>
public class SyncResult
{
    public string GedcomId { get; set; } = string.Empty;
    public string? GeniId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public SyncAction Action { get; set; }
    public int? MatchScore { get; set; }
    public string? RelationType { get; set; } // "child", "parent", "partner"
    public string? RelativeGeniId { get; set; } // Parent/child to link to
    public string? ErrorMessage { get; set; }
}

public enum SyncAction
{
    Matched,    // Found existing profile in Geni
    Created,    // Created new profile in Geni
    Skipped,    // Skipped (insufficient data)
    Error       // Failed to process
}
