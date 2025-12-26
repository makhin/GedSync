namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for removing duplicate values across locales.
/// Handles:
/// - Same value in multiple locales (keep in most appropriate one)
/// - Transliteration duplicates (Russian in ru and its transliteration in en)
/// - Case-insensitive duplicates
/// </summary>
public class DuplicateRemovalHandler : NameFixHandlerBase
{
    public override string Name => "DuplicateRemoval";
    public override int Order => 98;  // Near end, before final cleanup

    // Priority order for locales (higher priority = keep if duplicate)
    private static readonly Dictionary<string, int> LocalePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = 100,    // English highest priority
        ["ru"] = 90,        // Russian
        ["uk"] = 85,        // Ukrainian
        ["he"] = 80,        // Hebrew
        ["lt"] = 70,        // Lithuanian
        ["et"] = 70,        // Estonian
        ["lv"] = 70,        // Latvian
        ["pl"] = 70,        // Polish
        ["de"] = 70,        // German
        ["fr"] = 60,        // French
        ["es"] = 60,        // Spanish
        ["pt"] = 60,        // Portuguese
        ["it"] = 60         // Italian
    };

    public override void Handle(NameFixContext context)
    {
        // For each field, check for duplicates across locales
        foreach (var field in NameFields.All)
        {
            RemoveDuplicatesForField(context, field);
        }
    }

    private void RemoveDuplicatesForField(NameFixContext context, string field)
    {
        // Collect all values for this field across locales
        var valuesByLocale = new Dictionary<string, string>();

        foreach (var locale in context.Names.Keys)
        {
            var value = context.GetName(locale, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                valuesByLocale[locale] = value;
            }
        }

        if (valuesByLocale.Count < 2) return;

        // Group by normalized value to find duplicates
        var groups = valuesByLocale
            .GroupBy(kvp => NormalizeForComparison(kvp.Value))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            // Determine which locale should keep the value
            var entries = group.ToList();

            // Sort by priority (descending) and script appropriateness
            var sorted = entries
                .OrderByDescending(e => GetEffectivePriority(e.Key, e.Value))
                .ToList();

            // Keep the first (highest priority), remove from others
            var keepLocale = sorted[0].Key;

            for (int i = 1; i < sorted.Count; i++)
            {
                var removeLocale = sorted[i].Key;
                var removeValue = sorted[i].Value;

                // Check if this should be considered a removable duplicate
                if (!ShouldRemoveDuplicate(sorted[0].Value, removeValue, keepLocale, removeLocale))
                {
                    continue;
                }

                // Remove from this locale using mutable dictionary
                if (context.Names.TryGetValue(removeLocale, out var mutableFields) &&
                    mutableFields.ContainsKey(field))
                {
                    context.Changes.Add(new NameChange
                    {
                        Field = $"{field}[{removeLocale}]",
                        OldValue = removeValue,
                        NewValue = null,
                        Reason = $"Duplicate value exists in [{keepLocale}]",
                        Handler = Name
                    });

                    mutableFields.Remove(field);
                }
            }
        }
    }

    private bool ShouldRemoveDuplicate(string kept, string removed, string keepLocale, string removeLocale)
    {
        // Don't remove from language-specific locales that represent different languages
        // even if values are identical

        // Don't remove from Cyrillic language locales (uk, be, etc.) if kept in ru
        // These are different languages that may share names
        if (IsCyrillicLocale(keepLocale) && IsCyrillicLocale(removeLocale))
        {
            return false;
        }

        // Don't remove from Latin language locales if the name contains language-specific characters
        // (e.g., Šimkauskas in lt should not be removed even if same in en-US)
        if (IsLatinLanguageLocale(removeLocale) && Locales.IsEnglish(keepLocale))
        {
            // Check if the value contains language-specific diacritics
            if (ContainsLanguageSpecificChars(removed))
            {
                return false;
            }
        }

        // For same language duplicates, check if it's truly a duplicate
        return IsTrivialDuplicate(kept, removed, keepLocale, removeLocale);
    }

    private static bool ContainsLanguageSpecificChars(string text)
    {
        // Check for diacritics and special characters that indicate a non-English name
        foreach (var c in text)
        {
            // Extended Latin characters (not basic A-Z, a-z)
            if (c >= 0x00C0 && c <= 0x00FF) return true;  // Latin-1 Supplement (ä, ö, ü, etc.)
            if (c >= 0x0100 && c <= 0x024F) return true;  // Latin Extended-A and B (š, ž, ą, etc.)
        }
        return false;
    }

    private static bool IsLatinLanguageLocale(string locale)
    {
        return locale is "lt" or "et" or "lv" or "pl" or "de" or "fr" or "es" or "pt" or "it";
    }

    private string NormalizeForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        // Lowercase, remove accents, normalize whitespace
        var normalized = value.ToLowerInvariant().Trim();

        // Remove common diacritics for comparison
        normalized = RemoveDiacriticsSimple(normalized);

        return normalized;
    }

    private string RemoveDiacriticsSimple(string text)
    {
        // Simple diacritics removal for comparison purposes
        var replacements = new Dictionary<char, char>
        {
            ['á'] = 'a', ['à'] = 'a', ['ä'] = 'a', ['â'] = 'a', ['ã'] = 'a', ['å'] = 'a', ['ą'] = 'a',
            ['é'] = 'e', ['è'] = 'e', ['ë'] = 'e', ['ê'] = 'e', ['ę'] = 'e', ['ė'] = 'e',
            ['í'] = 'i', ['ì'] = 'i', ['ï'] = 'i', ['î'] = 'i', ['į'] = 'i',
            ['ó'] = 'o', ['ò'] = 'o', ['ö'] = 'o', ['ô'] = 'o', ['õ'] = 'o', ['ø'] = 'o',
            ['ú'] = 'u', ['ù'] = 'u', ['ü'] = 'u', ['û'] = 'u', ['ų'] = 'u', ['ū'] = 'u',
            ['ý'] = 'y', ['ÿ'] = 'y',
            ['ñ'] = 'n', ['ń'] = 'n',
            ['ç'] = 'c', ['ć'] = 'c', ['č'] = 'c',
            ['š'] = 's', ['ś'] = 's',
            ['ž'] = 'z', ['ź'] = 'z', ['ż'] = 'z',
            ['ł'] = 'l',
            ['ß'] = 's'
        };

        var result = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            result.Append(replacements.TryGetValue(c, out var replacement) ? replacement : c);
        }

        return result.ToString();
    }

    private int GetEffectivePriority(string locale, string value)
    {
        var basePriority = LocalePriority.TryGetValue(locale, out var p) ? p : 50;

        // Boost priority if script matches locale
        if (IsScriptAppropriateForLocale(value, locale))
        {
            basePriority += 10;
        }

        return basePriority;
    }

    private bool IsScriptAppropriateForLocale(string value, string locale)
    {
        var hasCyrillic = value.Any(c => ScriptDetector.IsCyrillic(c));
        var hasLatin = value.Any(c => ScriptDetector.IsLatinLetter(c));
        var hasHebrew = value.Any(c => ScriptDetector.IsHebrew(c));

        return locale switch
        {
            "ru" or "uk" => hasCyrillic,
            "he" => hasHebrew,
            "en-US" or "lt" or "et" or "lv" or "pl" or "de" or "fr" => hasLatin,
            _ => true
        };
    }

    private bool IsTrivialDuplicate(string kept, string removed, string keepLocale, string removeLocale)
    {
        // Same value in different scripts is NOT a trivial duplicate
        // (e.g., "Иван" and "Ivan" should both be kept in their respective locales)
        var keptHasCyrillic = kept.Any(c => ScriptDetector.IsCyrillic(c));
        var removedHasCyrillic = removed.Any(c => ScriptDetector.IsCyrillic(c));

        if (keptHasCyrillic != removedHasCyrillic)
            return false;  // Different scripts, keep both

        // Don't remove from language-specific Cyrillic locales (uk, ru, be, etc.)
        // even if values match - they represent different languages
        if (IsCyrillicLocale(keepLocale) && IsCyrillicLocale(removeLocale))
        {
            // Ukrainian names should stay in uk even if same as ru
            return false;
        }

        // Exact match (case insensitive) in non-Cyrillic locales
        if (kept.Equals(removed, StringComparison.OrdinalIgnoreCase))
            return true;

        // Same script, just case difference
        return true;
    }

    private static bool IsCyrillicLocale(string locale)
    {
        return locale is "ru" or "uk" or "be" or "bg" or "sr" or "mk" or "kk" or "ky";
    }
}
