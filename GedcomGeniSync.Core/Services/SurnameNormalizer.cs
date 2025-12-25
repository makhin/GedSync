using GedcomGeniSync.Services.Interfaces;

namespace GedcomGeniSync.Services;

/// <summary>
/// Normalizes surnames to base (masculine) form for comparison.
/// Handles Slavic surname patterns (Russian, Ukrainian, Polish, Belarusian, etc.)
/// </summary>
public class SurnameNormalizer : ISurnameNormalizer
{
    /// <summary>
    /// Feminine surname endings mapped to their masculine equivalents.
    /// Order matters: longer suffixes must come before shorter ones to avoid partial matches.
    /// </summary>
    private static readonly (string Feminine, string Masculine)[] SlavicSuffixes = new[]
    {
        // ===== CYRILLIC FORMS =====

        // Russian/Ukrainian adjective-based surnames (longest first)
        ("ская", "ский"),   // Чайковская → Чайковский
        ("цкая", "цкий"),   // Троцкая → Троцкий
        ("ная", "ный"),     // Красная → Красный
        ("ая", "ий"),       // Горькая → Горький (adjectives)

        // Standard Russian patronymic-style surnames
        ("ова", "ов"),      // Иванова → Иванов
        ("ева", "ев"),      // Медведева → Медведев
        ("ёва", "ёв"),      // Королёва → Королёв
        ("ина", "ин"),      // Путина → Путин
        ("ына", "ын"),      // Лисицына → Лисицын

        // Ukrainian surnames (unchanged for both genders)
        ("енко", "енко"),   // Шевченко (no change)
        ("ук", "ук"),       // Полищук (no change)
        ("юк", "юк"),       // Ковалюк (no change)
        ("ак", "ак"),       // Гайдак (no change)
        ("як", "як"),       // Гуляк (no change)

        // ===== LATIN/TRANSLITERATED FORMS =====

        // Polish surnames (Latin script)
        ("ska", "ski"),     // Kowalska → Kowalski
        ("cka", "cki"),     // Nowicka → Nowicki
        ("dzka", "dzki"),   // Zawadzka → Zawadzki
        ("na", "ny"),       // Czerwona → Czerwony (adjectives)

        // Russian transliterated forms
        ("skaya", "skiy"),  // Chaikovskaya → Chaikovskiy (must come before shorter forms)
        ("tskaya", "tskiy"), // Trotskaya → Trotskiy
        ("aya", "iy"),      // Gorskaya → Gorskiy (adjectives)
        ("ova", "ov"),      // Ivanova → Ivanov
        ("eva", "ev"),      // Medvedeva → Medvedev
        ("yova", "yov"),    // Korolyova → Korolyov
        ("ina", "in"),      // Putina → Putin
        ("yna", "yn"),      // Lisitsyna → Lisitsyn

        // Ukrainian transliterated (unchanged)
        ("enko", "enko"),   // Shevchenko (no change)
        ("uk", "uk"),       // Polishchuk (no change)
        ("yuk", "yuk"),     // Kovalyuk (no change)
        ("ak", "ak"),       // Haydak (no change)
        ("yak", "yak"),     // Gulyak (no change)
    };

    /// <summary>
    /// Surnames that should not be modified (they look feminine but aren't).
    /// Includes exceptional surnames that end in typical feminine suffixes but don't change.
    /// </summary>
    private static readonly HashSet<string> Exceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Names ending in -а/-я that are not feminine forms
        "Сковорода", "Skovoroda",
        "Кочерга", "Kocherga",
        "Лоза", "Loza",
        "Гроза", "Groza",
        "Сирота", "Sirota",

        // Ukrainian surnames unchanged for both genders
        "Шевченко", "Shevchenko",
        "Бондаренко", "Bondarenko",
        "Коваленко", "Kovalenko",
        "Ткаченко", "Tkachenko",
        "Savchenko", "Савченко",

        // Georgian surnames
        "Саакашвили", "Saakashvili",
        "Джугашвили", "Dzhugashvili",

        // Other exceptions
        "Франко", "Franko",
    };

    /// <summary>
    /// Normalizes a surname to its base (masculine) form.
    /// </summary>
    /// <param name="surname">The surname to normalize</param>
    /// <returns>Normalized surname in masculine form</returns>
    public string Normalize(string? surname)
    {
        if (string.IsNullOrWhiteSpace(surname))
            return string.Empty;

        var trimmed = surname.Trim();

        // Check exceptions first
        if (Exceptions.Contains(trimmed))
            return trimmed;

        // Try each suffix replacement (longest first due to array ordering)
        foreach (var (feminine, masculine) in SlavicSuffixes)
        {
            if (trimmed.EndsWith(feminine, StringComparison.OrdinalIgnoreCase))
            {
                // Don't change if feminine == masculine (Ukrainian surnames, etc.)
                if (feminine.Equals(masculine, StringComparison.OrdinalIgnoreCase))
                    return trimmed;

                // Replace suffix preserving the original case of the base
                var baseName = trimmed[..^feminine.Length];

                // Preserve the case pattern of the original suffix in the replacement
                var normalizedSuffix = PreserveCasePattern(
                    trimmed.Substring(trimmed.Length - feminine.Length),
                    masculine);

                return baseName + normalizedSuffix;
            }
        }

        // No matching suffix found, return as-is
        return trimmed;
    }

    /// <summary>
    /// Compares two surnames accounting for gender variations.
    /// </summary>
    /// <param name="surname1">First surname</param>
    /// <param name="surname2">Second surname</param>
    /// <returns>True if surnames match (ignoring gender suffix)</returns>
    public bool AreEquivalent(string? surname1, string? surname2)
    {
        if (string.IsNullOrWhiteSpace(surname1) && string.IsNullOrWhiteSpace(surname2))
            return true;

        if (string.IsNullOrWhiteSpace(surname1) || string.IsNullOrWhiteSpace(surname2))
            return false;

        var normalized1 = Normalize(surname1);
        var normalized2 = Normalize(surname2);

        return normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns similarity score between two surnames (0.0 - 1.0).
    /// Returns 1.0 for equivalent surnames, falls back to provided similarity function otherwise.
    /// </summary>
    public double GetSimilarity(string? surname1, string? surname2,
        Func<string, string, double> fallbackSimilarity)
    {
        if (AreEquivalent(surname1, surname2))
            return 1.0;

        // Normalize both and compare
        var norm1 = Normalize(surname1);
        var norm2 = Normalize(surname2);

        if (norm1.Equals(norm2, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Use fallback (Jaro-Winkler or other) for non-matching surnames
        return fallbackSimilarity(norm1, norm2);
    }

    /// <summary>
    /// Preserves the case pattern from the original suffix when applying the replacement.
    /// Examples:
    /// - "ova" with pattern "OVA" → "OV"
    /// - "ova" with pattern "Ova" → "Ov"
    /// - "ova" with pattern "ova" → "ov"
    /// </summary>
    private static string PreserveCasePattern(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement))
            return replacement;

        // All uppercase
        if (original.All(char.IsUpper))
            return replacement.ToUpperInvariant();

        // Title case (first letter upper)
        if (char.IsUpper(original[0]) && original.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c)))
        {
            return char.ToUpperInvariant(replacement[0]) +
                   (replacement.Length > 1 ? replacement.Substring(1).ToLowerInvariant() : "");
        }

        // All lowercase
        return replacement.ToLowerInvariant();
    }
}
