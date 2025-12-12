namespace GedcomGeniSync.Utils;

/// <summary>
/// Utility for normalizing GEDCOM IDs to standard format.
/// </summary>
public static class GedcomIdNormalizer
{
    /// <summary>
    /// Normalizes a GEDCOM ID to standard format with @ delimiters.
    /// Examples:
    /// - "I1" -> "@I1@"
    /// - "@I1@" -> "@I1@"
    /// - "@I1" -> "@I1@"
    /// - "I1@" -> "@I1@"
    /// - " I1 " -> "@I1@"
    /// </summary>
    /// <param name="id">The GEDCOM ID to normalize.</param>
    /// <returns>Normalized GEDCOM ID with @ delimiters.</returns>
    public static string Normalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return id;

        id = id.Trim();

        if (!id.StartsWith("@"))
            id = "@" + id;

        if (!id.EndsWith("@"))
            id = id + "@";

        return id;
    }
}
