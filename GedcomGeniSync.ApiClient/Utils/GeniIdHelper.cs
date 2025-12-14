using System.Text.RegularExpressions;

namespace GedcomGeniSync.ApiClient.Utils;

/// <summary>
/// Helper class for working with Geni Profile IDs in different formats
/// </summary>
public static class GeniIdHelper
{
    /// <summary>
    /// Extract numeric Geni ID from various formats
    /// Supports:
    /// - GEDCOM INDI format: @I6000000206529622827@
    /// - Geni RFN format: geni:6000000206529622827
    /// - Geni Profile format: profile-6000000206529622827
    /// - Raw numeric: 6000000206529622827
    /// </summary>
    /// <param name="id">ID in any supported format</param>
    /// <returns>Numeric ID as string, or null if format not recognized</returns>
    public static string? ExtractNumericId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        // Try GEDCOM INDI format: @I6000000206529622827@
        var indiMatch = Regex.Match(id, @"@I(\d+)@");
        if (indiMatch.Success)
            return indiMatch.Groups[1].Value;

        // Try Geni RFN format: geni:6000000206529622827
        var geniMatch = Regex.Match(id, @"geni:(\d+)");
        if (geniMatch.Success)
            return geniMatch.Groups[1].Value;

        // Try Geni Profile format: profile-6000000206529622827
        var profileMatch = Regex.Match(id, @"profile-(\d+)");
        if (profileMatch.Success)
            return profileMatch.Groups[1].Value;

        // Try raw numeric: 6000000206529622827
        if (Regex.IsMatch(id, @"^\d+$"))
            return id;

        return null;
    }

    /// <summary>
    /// Check if two IDs refer to the same Geni profile by comparing their numeric parts
    /// </summary>
    /// <param name="id1">First ID in any supported format</param>
    /// <param name="id2">Second ID in any supported format</param>
    /// <returns>True if both IDs contain the same numeric ID</returns>
    public static bool IsSameGeniProfile(string? id1, string? id2)
    {
        if (string.IsNullOrWhiteSpace(id1) || string.IsNullOrWhiteSpace(id2))
            return false;

        var numericId1 = ExtractNumericId(id1);
        var numericId2 = ExtractNumericId(id2);

        if (numericId1 == null || numericId2 == null)
            return false;

        return numericId1 == numericId2;
    }

    /// <summary>
    /// Convert numeric Geni ID to GEDCOM INDI format
    /// </summary>
    /// <param name="numericId">Numeric ID</param>
    /// <returns>GEDCOM INDI ID (e.g., "@I6000000206529622827@")</returns>
    public static string ToGedcomIndiId(string numericId)
    {
        if (string.IsNullOrWhiteSpace(numericId))
            throw new ArgumentException("Numeric ID cannot be null or empty", nameof(numericId));

        // If already in INDI format, return as-is
        if (numericId.StartsWith("@I") && numericId.EndsWith("@"))
            return numericId;

        // Extract numeric part if in other format
        var numeric = ExtractNumericId(numericId);
        if (numeric == null)
            throw new ArgumentException($"Cannot extract numeric ID from: {numericId}", nameof(numericId));

        return $"@I{numeric}@";
    }

    /// <summary>
    /// Convert numeric Geni ID to Geni RFN format
    /// </summary>
    /// <param name="numericId">Numeric ID</param>
    /// <returns>Geni RFN format (e.g., "geni:6000000206529622827")</returns>
    public static string ToGeniRfnFormat(string numericId)
    {
        if (string.IsNullOrWhiteSpace(numericId))
            throw new ArgumentException("Numeric ID cannot be null or empty", nameof(numericId));

        // If already in RFN format, return as-is
        if (numericId.StartsWith("geni:"))
            return numericId;

        // Extract numeric part if in other format
        var numeric = ExtractNumericId(numericId);
        if (numeric == null)
            throw new ArgumentException($"Cannot extract numeric ID from: {numericId}", nameof(numericId));

        return $"geni:{numeric}";
    }

    /// <summary>
    /// Convert numeric Geni ID to Geni Profile format
    /// </summary>
    /// <param name="numericId">Numeric ID</param>
    /// <returns>Geni Profile format (e.g., "profile-6000000206529622827")</returns>
    public static string ToGeniProfileFormat(string numericId)
    {
        if (string.IsNullOrWhiteSpace(numericId))
            throw new ArgumentException("Numeric ID cannot be null or empty", nameof(numericId));

        // If already in Profile format, return as-is
        if (numericId.StartsWith("profile-"))
            return numericId;

        // Extract numeric part if in other format
        var numeric = ExtractNumericId(numericId);
        if (numeric == null)
            throw new ArgumentException($"Cannot extract numeric ID from: {numericId}", nameof(numericId));

        return $"profile-{numeric}";
    }
}
