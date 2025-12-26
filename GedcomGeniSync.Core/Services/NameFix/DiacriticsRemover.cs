using System.Globalization;
using System.Text;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Utility for removing diacritical marks from Latin characters.
/// Converts extended Latin characters to basic ASCII Latin.
/// Examples: ä→a, ö→o, ü→u, ł→l, š→s, č→c, õ→o, ß→ss
/// </summary>
public static class DiacriticsRemover
{
    /// <summary>
    /// Special character replacements that can't be handled by Unicode normalization
    /// </summary>
    private static readonly Dictionary<char, string> SpecialReplacements = new()
    {
        // German
        ['ß'] = "ss",
        ['ẞ'] = "SS",

        // Polish
        ['ł'] = "l",
        ['Ł'] = "L",

        // Scandinavian/Nordic
        ['ø'] = "o",
        ['Ø'] = "O",
        ['æ'] = "ae",
        ['Æ'] = "AE",
        ['å'] = "a",
        ['Å'] = "A",

        // Icelandic
        ['ð'] = "d",
        ['Ð'] = "D",
        ['þ'] = "th",
        ['Þ'] = "TH",

        // Croatian/Serbian
        ['đ'] = "dj",
        ['Đ'] = "Dj",

        // Turkish
        ['ı'] = "i",
        ['İ'] = "I",

        // Estonian õ (o with tilde)
        ['õ'] = "o",
        ['Õ'] = "O",
    };

    /// <summary>
    /// Remove diacritics and convert to basic ASCII Latin.
    /// </summary>
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // First, handle special characters that can't be normalized
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (SpecialReplacements.TryGetValue(c, out var replacement))
            {
                sb.Append(replacement);
            }
            else
            {
                sb.Append(c);
            }
        }

        var processed = sb.ToString();

        // Use Unicode normalization to decompose characters
        // NFD splits characters like 'ä' into 'a' + combining diaeresis
        var normalized = processed.Normalize(NormalizationForm.FormD);

        // Remove combining diacritical marks (category: NonSpacingMark)
        var result = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Check if text contains only basic ASCII Latin letters (plus common punctuation)
    /// </summary>
    public static bool IsBasicLatin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                // Only allow basic Latin A-Z, a-z
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Check if text contains extended Latin characters (diacritics)
    /// </summary>
    public static bool HasDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var c in text)
        {
            // Check for special replacements
            if (SpecialReplacements.ContainsKey(c))
                return true;

            // Check for combining marks after normalization
            if (char.IsLetter(c) && c > 127)
            {
                var normalized = c.ToString().Normalize(NormalizationForm.FormD);
                if (normalized.Length > 1)
                    return true;

                // Character didn't decompose but is still extended Latin
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                    return true;
            }
        }

        return false;
    }
}
