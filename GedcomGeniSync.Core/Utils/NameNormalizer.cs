namespace GedcomGeniSync.Utils;

/// <summary>
/// Static utility for name normalization and transliteration
/// Used for pre-computing normalized names for efficient matching
/// </summary>
public static class NameNormalizer
{
    // Cyrillic to Latin transliteration map (same as in NameVariantsService)
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // Ukrainian specific
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g",
        // Upper case
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D",
        ['Е'] = "E", ['Ё'] = "Yo", ['Ж'] = "Zh", ['З'] = "Z", ['И'] = "I",
        ['Й'] = "Y", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N",
        ['О'] = "O", ['П'] = "P", ['Р'] = "R", ['С'] = "S", ['Т'] = "T",
        ['У'] = "U", ['Ф'] = "F", ['Х'] = "Kh", ['Ц'] = "Ts", ['Ч'] = "Ch",
        ['Ш'] = "Sh", ['Щ'] = "Shch", ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "",
        ['Э'] = "E", ['Ю'] = "Yu", ['Я'] = "Ya",
        ['І'] = "I", ['Ї'] = "Yi", ['Є'] = "Ye", ['Ґ'] = "G"
    };

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
        return transliterated
            .ToLowerInvariant()
            .Replace("-", "")
            .Replace("'", "")
            .Replace(".", "")
            .Replace(" ", "")
            .Trim();
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
            if (CyrillicToLatin.TryGetValue(c, out var replacement))
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
}
