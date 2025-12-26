using Unidecode.NET;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Transliteration service using Unidecode.NET library.
/// Converts any Unicode text to ASCII representation.
/// </summary>
public static class Transliterator
{
    /// <summary>
    /// Transliterate text to ASCII using Unidecode.NET library.
    /// Handles Cyrillic, Hebrew, Greek, and other scripts.
    /// </summary>
    public static string ToAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Unidecode converts any Unicode to ASCII approximation
        return text.Unidecode();
    }

    /// <summary>
    /// Transliterate Cyrillic text to Latin with proper name casing.
    /// Uses Unidecode.NET for the actual transliteration.
    /// </summary>
    public static string TransliterateCyrillic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Unidecode handles Cyrillic → Latin automatically
        var result = text.Unidecode();

        // Apply title case for proper names
        return ToTitleCase(result);
    }

    /// <summary>
    /// Remove diacritics from Latin text (ä→a, ö→o, š→s, etc.)
    /// Uses Unidecode.NET which handles all Unicode normalization.
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Unidecode removes all diacritics and converts to ASCII
        return text.Unidecode();
    }

    /// <summary>
    /// Check if text contains only basic ASCII Latin letters (A-Z, a-z).
    /// </summary>
    public static bool IsBasicLatin(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        return text.All(c => !char.IsLetter(c) || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    }

    /// <summary>
    /// Check if text needs transliteration (contains non-ASCII letters).
    /// </summary>
    public static bool NeedsTransliteration(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Any(c => char.IsLetter(c) && c > 127);
    }

    /// <summary>
    /// Convert text to Title Case for proper names.
    /// </summary>
    private static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                // Handle hyphenated names
                if (words[i].Contains('-'))
                {
                    var parts = words[i].Split('-');
                    words[i] = string.Join("-", parts.Select(p =>
                        p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1).ToLower() : p));
                }
                else
                {
                    words[i] = char.ToUpper(words[i][0]) +
                        (words[i].Length > 1 ? words[i].Substring(1).ToLower() : "");
                }
            }
        }
        return string.Join(" ", words);
    }
}
