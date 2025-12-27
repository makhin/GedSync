using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that resolves married surnames based on spouse's last name.
///
/// 1. For men or unmarried women (no spouse info):
///    - If MaidenName is empty, copy LastName to MaidenName (birth name = current name)
///
/// 2. For married women with both LastName and MaidenName:
///    - Compares both surnames with the spouse's last name
///    - The surname matching the spouse becomes LastName (married name)
///    - The other surname becomes MaidenName (birth name)
///
/// Example:
/// - Woman: LastName="Попова", MaidenName="Рыжова"
/// - Husband: LastName="Рыжов"
/// - Result: LastName="Рыжова" (matches husband), MaidenName="Попова" (birth name)
///
/// Handles Slavic feminine suffixes (Рыжов → Рыжова).
/// </summary>
public class MarriedSurnameHandler : NameFixHandlerBase
{
    public override string Name => "MarriedSurname";

    // Run after MaidenNameExtractHandler (12) but before PatronymicHandler (15)
    public override int Order => 14;

    public override void Handle(NameFixContext context)
    {
        var hasSpouseInfo = !string.IsNullOrWhiteSpace(context.SpouseLastName) ||
                            (context.SpouseLastNames != null && context.SpouseLastNames.Count > 0);

        // Case 1: Men or unmarried women - copy LastName to MaidenName if empty
        if (context.Gender == Gender.Male ||
            (context.Gender == Gender.Female && !hasSpouseInfo))
        {
            CopyLastNameToMaidenName(context);
            return;
        }

        // Case 2: Married women - resolve based on spouse's surname
        if (context.Gender == Gender.Female && hasSpouseInfo)
        {
            // Process primary fields
            ProcessPrimaryFields(context);

            // Process localized fields
            foreach (var locale in context.Names.Keys.ToList())
            {
                ProcessLocale(context, locale);
            }
        }
    }

    /// <summary>
    /// For men or unmarried women: copy LastName to MaidenName if MaidenName is empty.
    /// Birth name equals current name when person never married.
    /// </summary>
    private void CopyLastNameToMaidenName(NameFixContext context)
    {
        // Process primary fields
        if (!string.IsNullOrWhiteSpace(context.LastName) &&
            string.IsNullOrWhiteSpace(context.MaidenName))
        {
            context.MaidenName = context.LastName;
            context.Changes.Add(new NameChange
            {
                Field = "MaidenName",
                OldValue = null,
                NewValue = context.LastName,
                Reason = "Copied from LastName (birth name for unmarried person)",
                Handler = Name
            });
        }

        // Process localized fields
        foreach (var locale in context.Names.Keys.ToList())
        {
            var lastName = context.GetName(locale, NameFields.LastName);
            var maidenName = context.GetName(locale, NameFields.MaidenName);

            if (!string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(maidenName))
            {
                SetName(context, locale, NameFields.MaidenName, lastName,
                    "Copied from LastName (birth name for unmarried person)");
            }
        }
    }

    private void ProcessPrimaryFields(NameFixContext context)
    {
        var lastName = context.LastName;
        var maidenName = context.MaidenName;

        // Need both surnames to make a decision
        if (string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(maidenName))
            return;

        var spouseLastName = context.SpouseLastName;
        if (string.IsNullOrWhiteSpace(spouseLastName))
            return;

        // Check if surnames need to be swapped
        var shouldSwap = ShouldSwapSurnames(lastName, maidenName, spouseLastName);
        if (!shouldSwap) return;

        // Swap the surnames
        context.LastName = maidenName;
        context.MaidenName = lastName;

        context.Changes.Add(new NameChange
        {
            Field = "LastName",
            OldValue = lastName,
            NewValue = maidenName,
            Reason = $"Swapped with maiden name: '{maidenName}' matches spouse surname '{spouseLastName}'",
            Handler = Name
        });

        context.Changes.Add(new NameChange
        {
            Field = "MaidenName",
            OldValue = maidenName,
            NewValue = lastName,
            Reason = $"Swapped with last name: birth surname is '{lastName}'",
            Handler = Name
        });
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var lastName = context.GetName(locale, NameFields.LastName);
        var maidenName = context.GetName(locale, NameFields.MaidenName);

        // Need both surnames to make a decision
        if (string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(maidenName))
            return;

        // Get spouse's last name for this locale
        var spouseLastName = context.GetSpouseLastName(locale) ?? context.SpouseLastName;
        if (string.IsNullOrWhiteSpace(spouseLastName))
            return;

        // Check if surnames need to be swapped
        var shouldSwap = ShouldSwapSurnames(lastName, maidenName, spouseLastName);
        if (!shouldSwap) return;

        // Swap the surnames
        SetName(context, locale, NameFields.LastName, maidenName,
            $"Swapped with maiden name: '{maidenName}' matches spouse surname '{spouseLastName}'");
        SetName(context, locale, NameFields.MaidenName, lastName,
            $"Swapped with last name: birth surname is '{lastName}'");
    }

    /// <summary>
    /// Determines if LastName and MaidenName should be swapped.
    /// Returns true if MaidenName matches spouse's surname but LastName doesn't.
    /// </summary>
    private bool ShouldSwapSurnames(string lastName, string maidenName, string spouseLastName)
    {
        var lastNameMatchesSpouse = SurnameMatchesSpouse(lastName, spouseLastName);
        var maidenNameMatchesSpouse = SurnameMatchesSpouse(maidenName, spouseLastName);

        // Swap only if maiden matches spouse but last name doesn't
        return maidenNameMatchesSpouse && !lastNameMatchesSpouse;
    }

    /// <summary>
    /// Checks if a feminine surname matches the spouse's (masculine) surname.
    /// Handles Slavic naming patterns: Попов → Попова, Рыжов → Рыжова
    /// </summary>
    private bool SurnameMatchesSpouse(string feminineSurname, string spouseSurname)
    {
        if (string.IsNullOrWhiteSpace(feminineSurname) || string.IsNullOrWhiteSpace(spouseSurname))
            return false;

        // Normalize for comparison
        var feminine = feminineSurname.Trim();
        var masculine = spouseSurname.Trim();

        // Direct match (for non-Slavic or non-changing surnames like Ukrainian -ко)
        if (feminine.Equals(masculine, StringComparison.OrdinalIgnoreCase))
            return true;

        // Convert masculine to expected feminine form and compare
        var expectedFeminine = ConvertToFeminine(masculine);
        if (expectedFeminine != null &&
            expectedFeminine.Equals(feminine, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Also try the reverse: convert feminine to masculine and compare
        var expectedMasculine = ConvertToMasculine(feminine);
        if (expectedMasculine != null &&
            expectedMasculine.Equals(masculine, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convert masculine Slavic surname to feminine form
    /// </summary>
    private string? ConvertToFeminine(string masculine)
    {
        if (string.IsNullOrWhiteSpace(masculine)) return null;

        // Check if it's Cyrillic
        if (ScriptDetector.ContainsCyrillic(masculine))
        {
            return ConvertToFeminineCyrillic(masculine);
        }
        else
        {
            return ConvertToFeminineLatin(masculine);
        }
    }

    private string? ConvertToFeminineCyrillic(string masculine)
    {
        // -ский → -ская
        if (masculine.EndsWith("ский", StringComparison.OrdinalIgnoreCase))
            return masculine[..^4] + "ская";
        if (masculine.EndsWith("цкий", StringComparison.OrdinalIgnoreCase))
            return masculine[..^4] + "цкая";

        // -ий → -ая (for adjective surnames)
        if (masculine.EndsWith("ий", StringComparison.OrdinalIgnoreCase) && masculine.Length > 3)
            return masculine[..^2] + "ая";

        // -ов → -ова, -ев → -ева, -ёв → -ёва
        if (masculine.EndsWith("ов", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("ев", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("ёв", StringComparison.OrdinalIgnoreCase))
            return masculine + "а";

        // -ин → -ина, -ын → -ына
        if (masculine.EndsWith("ин", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("ын", StringComparison.OrdinalIgnoreCase))
            return masculine + "а";

        return null;
    }

    private string? ConvertToFeminineLatin(string masculine)
    {
        // -skiy/-sky → -skaya
        if (masculine.EndsWith("skiy", StringComparison.OrdinalIgnoreCase))
            return masculine[..^4] + "skaya";
        if (masculine.EndsWith("sky", StringComparison.OrdinalIgnoreCase))
            return masculine[..^3] + "skaya";
        if (masculine.EndsWith("tskiy", StringComparison.OrdinalIgnoreCase))
            return masculine[..^5] + "tskaya";

        // -iy → -aya
        if (masculine.EndsWith("iy", StringComparison.OrdinalIgnoreCase) && masculine.Length > 3)
            return masculine[..^2] + "aya";

        // -ov → -ova, -ev → -eva, -yov → -yova
        if (masculine.EndsWith("ov", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("ev", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("yov", StringComparison.OrdinalIgnoreCase))
            return masculine + "a";

        // -in → -ina, -yn → -yna
        if (masculine.EndsWith("in", StringComparison.OrdinalIgnoreCase) ||
            masculine.EndsWith("yn", StringComparison.OrdinalIgnoreCase))
            return masculine + "a";

        return null;
    }

    /// <summary>
    /// Convert feminine Slavic surname to masculine form
    /// </summary>
    private string? ConvertToMasculine(string feminine)
    {
        if (string.IsNullOrWhiteSpace(feminine)) return null;

        // Check if it's Cyrillic
        if (ScriptDetector.ContainsCyrillic(feminine))
        {
            return ConvertToMasculineCyrillic(feminine);
        }
        else
        {
            return ConvertToMasculineLatin(feminine);
        }
    }

    private string? ConvertToMasculineCyrillic(string feminine)
    {
        // -ская → -ский
        if (feminine.EndsWith("ская", StringComparison.OrdinalIgnoreCase))
            return feminine[..^4] + "ский";
        if (feminine.EndsWith("цкая", StringComparison.OrdinalIgnoreCase))
            return feminine[..^4] + "цкий";

        // -ая → -ий (for adjective surnames)
        if (feminine.EndsWith("ая", StringComparison.OrdinalIgnoreCase) && feminine.Length > 3)
            return feminine[..^2] + "ий";

        // -ова → -ов, -ева → -ев, -ёва → -ёв
        if (feminine.EndsWith("ова", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("ева", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("ёва", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];

        // -ина → -ин, -ына → -ын
        if (feminine.EndsWith("ина", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("ына", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];

        return null;
    }

    private string? ConvertToMasculineLatin(string feminine)
    {
        // -skaya → -skiy/-sky
        if (feminine.EndsWith("skaya", StringComparison.OrdinalIgnoreCase))
            return feminine[..^5] + "skiy";
        if (feminine.EndsWith("tskaya", StringComparison.OrdinalIgnoreCase))
            return feminine[..^6] + "tskiy";

        // -aya → -iy
        if (feminine.EndsWith("aya", StringComparison.OrdinalIgnoreCase) && feminine.Length > 4)
            return feminine[..^3] + "iy";

        // -ova → -ov, -eva → -ev, -yova → -yov
        if (feminine.EndsWith("ova", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("eva", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("yova", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];

        // -ina → -in, -yna → -yn
        if (feminine.EndsWith("ina", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];
        if (feminine.EndsWith("yna", StringComparison.OrdinalIgnoreCase))
            return feminine[..^1];

        return null;
    }
}
