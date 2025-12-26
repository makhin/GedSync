using GedcomGeniSync.Utils;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for Ukrainian names.
/// Handles:
/// - Detection of Ukrainian-specific letters (і, ї, є, ґ)
/// - Ukrainian surnames (which don't change by gender for -ко endings)
/// - Ukrainian transliteration rules (different from Russian)
/// </summary>
public class UkrainianHandler : NameFixHandlerBase
{
    public override string Name => "Ukrainian";
    public override int Order => 24;  // Before Lithuanian (25)

    // Ukrainian-specific letters that distinguish from Russian
    private static readonly char[] UkrainianSpecificChars = new[]
    {
        'і', 'І',  // Ukrainian i (not Russian и)
        'ї', 'Ї',  // Ukrainian yi
        'є', 'Є',  // Ukrainian ye
        'ґ', 'Ґ'   // Ukrainian g (rare)
    };

    // Common Ukrainian surname patterns
    private static readonly string[] UkrainianSurnameEndings = new[]
    {
        "енко", "ейко", "ченко", "шенко",  // Шевченко, Коваленко
        "чук", "щук",  // Ковальчук
        "ський", "цький",  // Вишневський
        "ак", "як",  // Полтавак
        "ів", "їв"  // Київ (rare in surnames)
    };

    public override void Handle(NameFixContext context)
    {
        // Check all locales for Ukrainian names
        var localesToCheck = context.Names.Keys.ToList();

        foreach (var locale in localesToCheck)
        {
            // Skip if already Ukrainian locale
            if (locale == Locales.Ukrainian) continue;

            ProcessLocale(context, locale);
        }

        // Also check primary fields
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

            // Only process Cyrillic text
            if (!ScriptDetector.ContainsCyrillic(value)) continue;

            // Check if this looks Ukrainian
            if (!IsLikelyUkrainian(value)) continue;

            // Check if Ukrainian locale already has this field
            var existingUk = context.GetName(Locales.Ukrainian, field);
            if (!string.IsNullOrWhiteSpace(existingUk)) continue;

            // Copy to Ukrainian locale
            SetName(context, Locales.Ukrainian, field, value,
                $"Ukrainian name detected and copied from [{locale}]");

            // If this was in Russian locale, we might want to keep it there too
            // (many Ukrainians have Russian versions of their names)
        }
    }

    private void CheckPrimaryFields(NameFixContext context)
    {
        // Check if primary fields contain Ukrainian text
        CheckAndCopyToUkrainian(context, context.FirstName, NameFields.FirstName);
        CheckAndCopyToUkrainian(context, context.LastName, NameFields.LastName);
        CheckAndCopyToUkrainian(context, context.MiddleName, NameFields.MiddleName);
        CheckAndCopyToUkrainian(context, context.MaidenName, NameFields.MaidenName);
    }

    private void CheckAndCopyToUkrainian(NameFixContext context, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!ScriptDetector.ContainsCyrillic(value)) return;
        if (!IsLikelyUkrainian(value)) return;

        var existingUk = context.GetName(Locales.Ukrainian, field);
        if (!string.IsNullOrWhiteSpace(existingUk)) return;

        SetName(context, Locales.Ukrainian, field, value,
            "Ukrainian name detected from primary field");
    }

    /// <summary>
    /// Check if a Cyrillic name is likely Ukrainian (vs Russian)
    /// </summary>
    private bool IsLikelyUkrainian(string text)
    {
        // Check for Ukrainian-specific characters (definitive)
        if (text.Any(c => UkrainianSpecificChars.Contains(c)))
            return true;

        // Check for common Ukrainian surname patterns
        var lower = text.ToLowerInvariant();
        foreach (var ending in UkrainianSurnameEndings)
        {
            if (lower.EndsWith(ending))
            {
                // Additional check: -енко is very common in Ukrainian
                if (ending == "енко" || ending == "ченко" || ending == "шенко")
                    return true;

                // Other endings need more context
                // For now, return true for -чук as well
                if (ending == "чук" || ending == "щук")
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Transliterate Ukrainian text to Latin using Ukrainian rules.
    /// Different from Russian: і→i, и→y, є→ye, ї→yi, etc.
    /// </summary>
    public static string TransliterateUkrainian(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            result.Append(TransliterateUkrainianChar(c));
        }

        return result.ToString();
    }

    private static string TransliterateUkrainianChar(char c)
    {
        return c switch
        {
            // Ukrainian-specific
            'і' => "i",
            'І' => "I",
            'ї' => "yi",
            'Ї' => "Yi",
            'є' => "ye",
            'Є' => "Ye",
            'ґ' => "g",
            'Ґ' => "G",

            // Different from Russian transliteration
            'и' => "y",  // Ukrainian и is like Russian ы
            'И' => "Y",

            // Same as Russian
            'а' => "a", 'А' => "A",
            'б' => "b", 'Б' => "B",
            'в' => "v", 'В' => "V",
            'г' => "h", 'Г' => "H",  // Ukrainian г is h, not g
            'д' => "d", 'Д' => "D",
            'е' => "e", 'Е' => "E",
            'ж' => "zh", 'Ж' => "Zh",
            'з' => "z", 'З' => "Z",
            'й' => "y", 'Й' => "Y",
            'к' => "k", 'К' => "K",
            'л' => "l", 'Л' => "L",
            'м' => "m", 'М' => "M",
            'н' => "n", 'Н' => "N",
            'о' => "o", 'О' => "O",
            'п' => "p", 'П' => "P",
            'р' => "r", 'Р' => "R",
            'с' => "s", 'С' => "S",
            'т' => "t", 'Т' => "T",
            'у' => "u", 'У' => "U",
            'ф' => "f", 'Ф' => "F",
            'х' => "kh", 'Х' => "Kh",
            'ц' => "ts", 'Ц' => "Ts",
            'ч' => "ch", 'Ч' => "Ch",
            'ш' => "sh", 'Ш' => "Sh",
            'щ' => "shch", 'Щ' => "Shch",
            'ь' => "",  // Soft sign
            'ю' => "yu", 'Ю' => "Yu",
            'я' => "ya", 'Я' => "Ya",

            // Apostrophe (Ukrainian uses it)
            ''' => "",
            '\'' => "",

            _ => c.ToString()
        };
    }
}
