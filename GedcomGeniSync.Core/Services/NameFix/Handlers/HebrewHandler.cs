namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for Hebrew names.
/// Handles:
/// - Detection of Hebrew script
/// - Copying Hebrew text to he locale
/// - Basic transliteration to Latin for en-US
/// </summary>
public class HebrewHandler : NameFixHandlerBase
{
    public override string Name => "Hebrew";
    public override int Order => 28;  // After other language handlers

    public override void Handle(NameFixContext context)
    {
        // Check all locales for Hebrew names
        var localesToCheck = context.Names.Keys.ToList();

        foreach (var locale in localesToCheck)
        {
            if (locale == Locales.Hebrew) continue;
            ProcessLocale(context, locale);
        }

        // Check primary fields
        CheckPrimaryFields(context);
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Check if contains Hebrew
            if (!ContainsHebrew(value)) continue;

            // Check if Hebrew locale already has this field
            var existingHe = context.GetName(Locales.Hebrew, field);
            if (!string.IsNullOrWhiteSpace(existingHe)) continue;

            // Extract Hebrew portion
            var hebrewPart = ExtractHebrew(value);
            if (string.IsNullOrWhiteSpace(hebrewPart)) continue;

            // Copy to Hebrew locale
            SetName(context, Locales.Hebrew, field, hebrewPart,
                $"Hebrew name detected and copied from [{locale}]");
        }
    }

    private void CheckPrimaryFields(NameFixContext context)
    {
        CheckAndCopyToHebrew(context, context.FirstName, NameFields.FirstName);
        CheckAndCopyToHebrew(context, context.LastName, NameFields.LastName);
        CheckAndCopyToHebrew(context, context.MiddleName, NameFields.MiddleName);
        CheckAndCopyToHebrew(context, context.MaidenName, NameFields.MaidenName);
    }

    private void CheckAndCopyToHebrew(NameFixContext context, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!ContainsHebrew(value)) return;

        var existingHe = context.GetName(Locales.Hebrew, field);
        if (!string.IsNullOrWhiteSpace(existingHe)) return;

        var hebrewPart = ExtractHebrew(value);
        if (string.IsNullOrWhiteSpace(hebrewPart)) return;

        SetName(context, Locales.Hebrew, field, hebrewPart,
            "Hebrew name detected from primary field");
    }

    /// <summary>
    /// Check if text contains Hebrew characters
    /// </summary>
    private static bool ContainsHebrew(string text)
    {
        return text.Any(c => ScriptDetector.IsHebrew(c));
    }

    /// <summary>
    /// Extract only Hebrew characters from mixed text
    /// </summary>
    private static string ExtractHebrew(string text)
    {
        var hebrewChars = text.Where(c => ScriptDetector.IsHebrew(c) || char.IsWhiteSpace(c) || c == '-' || c == '\'');
        return new string(hebrewChars.ToArray()).Trim();
    }

    /// <summary>
    /// Basic Hebrew to Latin transliteration.
    /// Note: Hebrew transliteration is complex and has multiple standards.
    /// This is a simplified version.
    /// </summary>
    public static string TransliterateHebrew(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            result.Append(TransliterateHebrewChar(c));
        }

        return result.ToString();
    }

    private static string TransliterateHebrewChar(char c)
    {
        // Hebrew Unicode range: U+0590 to U+05FF
        return c switch
        {
            'א' => "",      // Alef (silent or glottal stop)
            'ב' => "v",     // Bet (v/b)
            'ג' => "g",     // Gimel
            'ד' => "d",     // Dalet
            'ה' => "h",     // He
            'ו' => "v",     // Vav (v/o/u)
            'ז' => "z",     // Zayin
            'ח' => "ch",    // Chet
            'ט' => "t",     // Tet
            'י' => "y",     // Yod (y/i)
            'כ' => "k",     // Kaf (k/kh)
            'ך' => "k",     // Final Kaf
            'ל' => "l",     // Lamed
            'מ' => "m",     // Mem
            'ם' => "m",     // Final Mem
            'נ' => "n",     // Nun
            'ן' => "n",     // Final Nun
            'ס' => "s",     // Samekh
            'ע' => "",      // Ayin (silent or glottal)
            'פ' => "f",     // Pe (p/f)
            'ף' => "f",     // Final Pe
            'צ' => "tz",    // Tsadi
            'ץ' => "tz",    // Final Tsadi
            'ק' => "k",     // Qof
            'ר' => "r",     // Resh
            'ש' => "sh",    // Shin (sh/s)
            'ת' => "t",     // Tav

            // Vowel points (nikkud) - usually omitted
            '\u05B0' => "e",  // Shva
            '\u05B1' => "e",  // Hataf Segol
            '\u05B2' => "a",  // Hataf Patah
            '\u05B3' => "o",  // Hataf Qamats
            '\u05B4' => "i",  // Hiriq
            '\u05B5' => "e",  // Tsere
            '\u05B6' => "e",  // Segol
            '\u05B7' => "a",  // Patah
            '\u05B8' => "a",  // Qamats
            '\u05B9' => "o",  // Holam
            '\u05BB' => "u",  // Qubuts
            '\u05BC' => "",   // Dagesh

            _ when char.IsWhiteSpace(c) => " ",
            _ when c == '-' || c == '\'' => c.ToString(),
            _ => ""
        };
    }
}
