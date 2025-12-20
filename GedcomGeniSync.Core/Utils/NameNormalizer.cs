namespace GedcomGeniSync.Utils;

/// <summary>
/// Static utility for name normalization and transliteration
/// Used for pre-computing normalized names for efficient matching
/// </summary>
public static class NameNormalizer
{

    /// <summary>
    /// Normalize a name for comparison (transliterate, lowercase, remove punctuation)
    /// This is the main method used to populate NormalizedFirstName/NormalizedLastName
    /// </summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Transliterate to common alphabet
        var transliterated = Transliterate(name);

        // Normalize: lowercase and remove punctuation
        var normalized = RemovePunctuation(transliterated.ToLowerInvariant());

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    /// <summary>
    /// Transliterate text from Cyrillic to Latin
    /// </summary>
    public static string Transliterate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            if (TransliterationConstants.CyrillicToLatin.TryGetValue(c, out var replacement))
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static string RemovePunctuation(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var c in value)
        {
            switch (c)
            {
                case '-':
                case '\'':
                case '.':
                case ' ':
                    continue;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}
