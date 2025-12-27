using System.Text.RegularExpressions;

namespace GedcomGeniSync.Utils;

/// <summary>
/// Utility for normalizing GEDCOM IDs to standard format.
/// </summary>
public static partial class GedcomIdNormalizer
{
    private static readonly Regex GeniPattern = GeniPatternRegex();
    private static readonly Regex ProfilePattern = ProfilePatternRegex();

    /// <summary>
    /// Normalizes a GEDCOM ID to standard format with @ delimiters.
    /// Examples:
    /// - "I1" -> "@I1@"
    /// - "@I1@" -> "@I1@"
    /// - "@I1" -> "@I1@"
    /// - "I1@" -> "@I1@"
    /// - " I1 " -> "@I1@"
    /// - "\@I1@" -> "@I1@" (strips backslash escape from System.CommandLine)
    /// - "geni:6000000206529622827" -> "@I6000000206529622827@"
    /// - "profile-6000000206529622827" -> "@I6000000206529622827@"
    /// </summary>
    /// <param name="id">The GEDCOM ID to normalize.</param>
    /// <returns>Normalized GEDCOM ID with @ delimiters.</returns>
    public static string Normalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return id;

        id = id.Trim();

        // Handle geni:123456 format
        var geniMatch = GeniPattern.Match(id);
        if (geniMatch.Success)
            return $"@I{geniMatch.Groups[1].Value}@";

        // Handle profile-123456 format
        var profileMatch = ProfilePattern.Match(id);
        if (profileMatch.Success)
            return $"@I{profileMatch.Groups[1].Value}@";

        // Remove leading backslash (used to escape @ in command-line args)
        if (id.StartsWith("\\@"))
            id = id.Substring(1);

        if (!id.StartsWith("@"))
            id = "@" + id;

        if (!id.EndsWith("@"))
            id = id + "@";

        return id;
    }

    [GeneratedRegex(@"^geni:(\d+)$", RegexOptions.Compiled)]
    private static partial Regex GeniPatternRegex();

    [GeneratedRegex(@"^profile-(\d+)$", RegexOptions.Compiled)]
    private static partial Regex ProfilePatternRegex();
}
