using Cyrillic.Convert;
using Unidecode.NET;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Transliteration service combining:
/// - Cyrillic.Convert for Slavic languages (BGN/PCGN standard)
/// - Unidecode.NET for other scripts (Hebrew, Greek, etc.)
/// </summary>
public static class Transliterator
{
    private static readonly Conversion _conversion = new();

    /// <summary>
    /// Transliterate text to ASCII.
    /// Uses BGN/PCGN for Cyrillic, Unidecode for other scripts.
    /// </summary>
    public static string ToAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Check if text contains Cyrillic - use BGN/PCGN
        if (ContainsCyrillic(text))
        {
            return TransliterateCyrillicBgn(text);
        }

        // For other scripts (Hebrew, Greek, etc.) - use Unidecode
        return text.Unidecode();
    }

    /// <summary>
    /// Transliterate Cyrillic text to Latin using BGN/PCGN standard.
    /// Detects language (Russian/Ukrainian/Belarusian) automatically.
    /// </summary>
    public static string TransliterateCyrillic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = TransliterateCyrillicBgn(text);

        // Apply title case for proper names
        return ToTitleCase(result);
    }

    /// <summary>
    /// Transliterate Russian text using BGN/PCGN.
    /// </summary>
    public static string TransliterateRussian(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return _conversion.RussianCyrillicToLatin(text);
    }

    /// <summary>
    /// Transliterate Ukrainian text using BGN/PCGN.
    /// </summary>
    public static string TransliterateUkrainian(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return _conversion.UkrainianCyrillicToLatin(text);
    }

    /// <summary>
    /// Transliterate Belarusian text using BGN/PCGN.
    /// </summary>
    public static string TransliterateBelarusian(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return _conversion.BelarusianCyrillicToLatin(text);
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
    /// Check if text contains Cyrillic characters.
    /// </summary>
    private static bool ContainsCyrillic(string text)
    {
        return text.Any(c => (c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F));
    }

    /// <summary>
    /// Detect language and transliterate using appropriate BGN/PCGN rules.
    /// </summary>
    private static string TransliterateCyrillicBgn(string text)
    {
        // Detect Ukrainian by specific letters: і, ї, є, ґ
        if (text.Any(c => c == 'і' || c == 'І' || c == 'ї' || c == 'Ї' ||
                         c == 'є' || c == 'Є' || c == 'ґ' || c == 'Ґ'))
        {
            return _conversion.UkrainianCyrillicToLatin(text);
        }

        // Detect Belarusian by specific letters: ў
        if (text.Any(c => c == 'ў' || c == 'Ў'))
        {
            return _conversion.BelarusianCyrillicToLatin(text);
        }

        // Default to Russian
        return _conversion.RussianCyrillicToLatin(text);
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
