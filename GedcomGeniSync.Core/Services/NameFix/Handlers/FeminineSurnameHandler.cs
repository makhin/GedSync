using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Interfaces;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that fixes feminine surnames in Slavic languages.
/// For example: If a female has surname "Попов", it should be "Попова".
/// Uses the existing ISurnameNormalizer service.
/// </summary>
public class FeminineSurnameHandler : NameFixHandlerBase
{
    private readonly ISurnameNormalizer? _surnameNormalizer;

    public override string Name => "FeminineSurname";
    public override int Order => 40;

    /// <summary>
    /// Constructor with optional surname normalizer dependency
    /// </summary>
    public FeminineSurnameHandler(ISurnameNormalizer? surnameNormalizer = null)
    {
        _surnameNormalizer = surnameNormalizer;
    }

    public override void Handle(NameFixContext context)
    {
        // Only process for females
        if (context.Gender != Gender.Female) return;

        // Process Russian locale
        ProcessLocale(context, Locales.Russian, isCyrillic: true);

        // Process English locale (for transliterated Russian surnames)
        ProcessLocale(context, Locales.PreferredEnglish, isCyrillic: false);
        ProcessLocale(context, Locales.EnglishShort, isCyrillic: false);

        // Process primary surname fields
        ProcessPrimarySurnames(context);
    }

    private void ProcessLocale(NameFixContext context, string locale, bool isCyrillic)
    {
        foreach (var field in NameFields.SurnameFields)
        {
            var value = context.GetName(locale, field);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var corrected = CorrectFeminineSurname(value, isCyrillic);
            if (corrected != null && !corrected.Equals(value, StringComparison.Ordinal))
            {
                SetName(context, locale, field, corrected,
                    $"Corrected feminine surname: '{value}' -> '{corrected}'");
            }
        }
    }

    private void ProcessPrimarySurnames(NameFixContext context)
    {
        if (context.Gender != Gender.Female) return;

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            var isCyrillic = ScriptDetector.ContainsCyrillic(context.LastName);
            var corrected = CorrectFeminineSurname(context.LastName, isCyrillic);
            if (corrected != null && !corrected.Equals(context.LastName, StringComparison.Ordinal))
            {
                var old = context.LastName;
                context.LastName = corrected;
                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = old,
                    NewValue = corrected,
                    Reason = $"Corrected feminine surname: '{old}' -> '{corrected}'",
                    Handler = Name
                });
            }
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName))
        {
            var isCyrillic = ScriptDetector.ContainsCyrillic(context.MaidenName);
            var corrected = CorrectFeminineSurname(context.MaidenName, isCyrillic);
            if (corrected != null && !corrected.Equals(context.MaidenName, StringComparison.Ordinal))
            {
                var old = context.MaidenName;
                context.MaidenName = corrected;
                context.Changes.Add(new NameChange
                {
                    Field = "MaidenName",
                    OldValue = old,
                    NewValue = corrected,
                    Reason = $"Corrected feminine surname: '{old}' -> '{corrected}'",
                    Handler = Name
                });
            }
        }
    }

    /// <summary>
    /// Correct a surname to its feminine form
    /// </summary>
    private string? CorrectFeminineSurname(string surname, bool isCyrillic)
    {
        if (string.IsNullOrWhiteSpace(surname)) return null;

        var trimmed = surname.Trim();

        // Use injected normalizer if available (for testing)
        if (_surnameNormalizer != null)
        {
            // The normalizer converts to masculine form, so we need the reverse
            // Check if surname is already feminine
            var normalized = _surnameNormalizer.Normalize(trimmed);
            if (!normalized.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                // Surname was normalized (it was feminine), so it's already correct
                return null;
            }
        }

        // Apply feminine suffix rules
        return isCyrillic
            ? ApplyCyrillicFeminineSuffix(trimmed)
            : ApplyLatinFeminineSuffix(trimmed);
    }

    /// <summary>
    /// Apply feminine suffix to Cyrillic surname
    /// </summary>
    private string? ApplyCyrillicFeminineSuffix(string surname)
    {
        // Check if already feminine
        if (IsFeminineSurnameCyrillic(surname)) return null;

        // Check exceptions (surnames that don't change by gender)
        if (IsExceptionSurnameCyrillic(surname)) return null;

        // Apply transformations
        // Russian/Ukrainian adjective-based surnames
        if (surname.EndsWith("ский", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "ский", "ская");
        if (surname.EndsWith("цкий", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "цкий", "цкая");
        if (surname.EndsWith("ний", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "ний", "няя");
        if (surname.EndsWith("ий", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "ий", "ая");

        // Standard patronymic-style surnames
        if (surname.EndsWith("ов", StringComparison.OrdinalIgnoreCase))
            return surname + "а";
        if (surname.EndsWith("ев", StringComparison.OrdinalIgnoreCase))
            return surname + "а";
        if (surname.EndsWith("ёв", StringComparison.OrdinalIgnoreCase))
            return surname + "а";
        if (surname.EndsWith("ин", StringComparison.OrdinalIgnoreCase))
            return surname + "а";
        if (surname.EndsWith("ын", StringComparison.OrdinalIgnoreCase))
            return surname + "а";

        // No matching pattern
        return null;
    }

    /// <summary>
    /// Apply feminine suffix to Latin (transliterated) surname
    /// </summary>
    private string? ApplyLatinFeminineSuffix(string surname)
    {
        // Check if already feminine
        if (IsFeminineSurnameLatin(surname)) return null;

        // Check exceptions
        if (IsExceptionSurnameLatin(surname)) return null;

        // Apply transformations (transliterated versions)
        if (surname.EndsWith("skiy", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "skiy", "skaya");
        if (surname.EndsWith("sky", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "sky", "skaya");
        if (surname.EndsWith("tskiy", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "tskiy", "tskaya");
        if (surname.EndsWith("iy", StringComparison.OrdinalIgnoreCase))
            return ReplaceEnding(surname, "iy", "aya");

        if (surname.EndsWith("ov", StringComparison.OrdinalIgnoreCase))
            return surname + "a";
        if (surname.EndsWith("ev", StringComparison.OrdinalIgnoreCase))
            return surname + "a";
        if (surname.EndsWith("yov", StringComparison.OrdinalIgnoreCase))
            return surname + "a";
        if (surname.EndsWith("in", StringComparison.OrdinalIgnoreCase))
            return surname + "a";
        if (surname.EndsWith("yn", StringComparison.OrdinalIgnoreCase))
            return surname + "a";

        return null;
    }

    private bool IsFeminineSurnameCyrillic(string surname)
    {
        // Already has feminine ending
        return surname.EndsWith("ова", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ева", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ёва", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ина", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ына", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ская", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("цкая", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("няя", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ая", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFeminineSurnameLatin(string surname)
    {
        return surname.EndsWith("ova", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("eva", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("yova", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ina", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("yna", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("skaya", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("tskaya", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("aya", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExceptionSurnameCyrillic(string surname)
    {
        var exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Ukrainian surnames that don't change
            "Шевченко", "Коваленко", "Бондаренко", "Ткаченко", "Кравченко",
            "Петренко", "Мельниченко", "Федоренко", "Савченко", "Марченко",
            // -ко endings generally don't change

            // Georgian surnames
            "Джугашвили", "Сталин", "Берия",

            // Other invariant surnames
            "Сковорода", "Кочерга", "Живаго", "Дурново", "Хитрово",

            // Common -ых/-их surnames
            "Черных", "Белых", "Красных", "Сухих", "Долгих", "Седых"
        };

        // Check direct match
        if (exceptions.Contains(surname)) return true;

        // Ukrainian -ко surnames don't change
        if (surname.EndsWith("ко", StringComparison.OrdinalIgnoreCase)) return true;

        // -ых/-их surnames don't change
        if (surname.EndsWith("ых", StringComparison.OrdinalIgnoreCase) ||
            surname.EndsWith("их", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsExceptionSurnameLatin(string surname)
    {
        var exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shevchenko", "Kovalenko", "Bondarenko", "Tkachenko", "Kravchenko",
            "Petrenko", "Melnichenko", "Fedorenko", "Savchenko", "Marchenko",
            "Dzhugashvili", "Stalin", "Beria",
            "Skovoroda", "Kocherga", "Zhivago", "Durnovo", "Khitrovo",
            "Chernykh", "Belykh", "Krasnykh", "Sukhikh", "Dolgikh", "Sedykh"
        };

        if (exceptions.Contains(surname)) return true;

        // -ko endings (Ukrainian)
        if (surname.EndsWith("ko", StringComparison.OrdinalIgnoreCase)) return true;

        // -ykh/-ikh surnames
        if (surname.EndsWith("ykh", StringComparison.OrdinalIgnoreCase) ||
            surname.EndsWith("ikh", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string ReplaceEnding(string surname, string oldEnding, string newEnding)
    {
        var baseName = surname.Substring(0, surname.Length - oldEnding.Length);

        // Preserve case pattern from original ending
        if (char.IsUpper(surname[surname.Length - oldEnding.Length]))
        {
            newEnding = char.ToUpper(newEnding[0]) + newEnding.Substring(1);
        }

        return baseName + newEnding;
    }
}
