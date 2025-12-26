using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that extracts suffixes (Jr., Sr., III, etc.) from names
/// and moves them to the suffix field.
/// </summary>
public class SuffixExtractHandler : NameFixHandlerBase
{
    public override string Name => "SuffixExtract";
    public override int Order => 11;

    // Common suffixes
    private static readonly string[] Suffixes = new[]
    {
        // Generational (English)
        "Jr.", "Jr", "Junior",
        "Sr.", "Sr", "Senior",
        "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
        "2nd", "3rd", "4th", "5th",
        // Professional
        "Esq.", "Esq",
        "PhD", "Ph.D.", "Ph.D",
        "MD", "M.D.", "M.D",
        "JD", "J.D.", "J.D",
        "DDS", "D.D.S.",
        "CPA", "C.P.A."
    };

    // Regex to match suffix at the end of a name
    // Suffixes are usually preceded by comma or space
    private readonly Regex _suffixRegex;

    public SuffixExtractHandler()
    {
        // Build regex: name followed by optional comma/space and suffix
        var suffixPattern = @"[,\s]+(" + string.Join("|", Suffixes.Select(Regex.Escape)) + @")\.?$";
        _suffixRegex = new Regex(suffixPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public override void Handle(NameFixContext context)
    {
        // Check LastName first (most common place for suffixes)
        ExtractFromLastName(context);

        // Check FirstName (less common but possible)
        ExtractFromFirstName(context);

        // Check localized names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ExtractFromLocale(context, locale);
        }
    }

    private void ExtractFromLastName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LastName)) return;
        if (!string.IsNullOrWhiteSpace(context.Suffix)) return; // Already has suffix

        var (suffix, remainingName) = ExtractSuffix(context.LastName);
        if (suffix == null) return;

        context.Changes.Add(new NameChange
        {
            Field = "LastName",
            OldValue = context.LastName,
            NewValue = remainingName,
            Reason = $"Extracted suffix '{suffix}'",
            Handler = Name
        });

        context.LastName = remainingName;
        context.Suffix = suffix;

        context.Changes.Add(new NameChange
        {
            Field = "Suffix",
            OldValue = null,
            NewValue = suffix,
            Reason = "Suffix extracted from last name",
            Handler = Name
        });
    }

    private void ExtractFromFirstName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName)) return;
        if (!string.IsNullOrWhiteSpace(context.Suffix)) return;

        var (suffix, remainingName) = ExtractSuffix(context.FirstName);
        if (suffix == null) return;

        context.Changes.Add(new NameChange
        {
            Field = "FirstName",
            OldValue = context.FirstName,
            NewValue = remainingName,
            Reason = $"Extracted suffix '{suffix}'",
            Handler = Name
        });

        context.FirstName = remainingName;
        context.Suffix = suffix;

        context.Changes.Add(new NameChange
        {
            Field = "Suffix",
            OldValue = null,
            NewValue = suffix,
            Reason = "Suffix extracted from first name",
            Handler = Name
        });
    }

    private void ExtractFromLocale(NameFixContext context, string locale)
    {
        // Check last_name in locale
        var lastName = context.GetName(locale, NameFields.LastName);
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            var existingSuffix = context.GetName(locale, NameFields.Suffix);
            if (string.IsNullOrWhiteSpace(existingSuffix))
            {
                var (suffix, remainingName) = ExtractSuffix(lastName);
                if (suffix != null)
                {
                    SetName(context, locale, NameFields.LastName, remainingName,
                        $"Extracted suffix '{suffix}'");
                    SetName(context, locale, NameFields.Suffix, suffix,
                        "Suffix extracted from last name");
                }
            }
        }

        // Check first_name in locale
        var firstName = context.GetName(locale, NameFields.FirstName);
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            var existingSuffix = context.GetName(locale, NameFields.Suffix);
            if (string.IsNullOrWhiteSpace(existingSuffix))
            {
                var (suffix, remainingName) = ExtractSuffix(firstName);
                if (suffix != null)
                {
                    SetName(context, locale, NameFields.FirstName, remainingName,
                        $"Extracted suffix '{suffix}'");
                    SetName(context, locale, NameFields.Suffix, suffix,
                        "Suffix extracted from first name");
                }
            }
        }
    }

    private (string? Suffix, string RemainingName) ExtractSuffix(string name)
    {
        var match = _suffixRegex.Match(name);
        if (!match.Success)
            return (null, name);

        var suffix = match.Groups[1].Value;
        var remaining = name.Substring(0, match.Index).Trim();

        // Normalize suffix
        suffix = NormalizeSuffix(suffix);

        return (suffix, remaining);
    }

    private static string NormalizeSuffix(string suffix)
    {
        // Standardize common suffixes
        var upper = suffix.ToUpperInvariant().TrimEnd('.');

        return upper switch
        {
            "JR" or "JUNIOR" => "Jr.",
            "SR" or "SENIOR" => "Sr.",
            "ESQ" => "Esq.",
            "PHD" or "PH.D" => "Ph.D.",
            "MD" or "M.D" => "M.D.",
            "JD" or "J.D" => "J.D.",
            _ => suffix // Keep as is for Roman numerals, etc.
        };
    }
}
