using GedcomGeniSync.Services.Interfaces;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects potential typos in names using CSV dictionaries.
/// Uses givenname_similar_names.csv and surname_similar_names.csv to find
/// if a name is a known variant or possible typo of a canonical name.
///
/// Workflow:
/// 1. Normalize the name (lowercase, remove diacritics)
/// 2. Look up in variants dictionary
/// 3. If found as a variant, suggest the canonical form
/// 4. If not found and similar to known names, flag as possible typo
/// </summary>
public class TypoDetectionHandler : NameFixHandlerBase
{
    public override string Name => "TypoDetection";
    public override int Order => 96;  // Near end, after capitalization fixes

    private readonly INameVariantsService? _variantsService;

    public TypoDetectionHandler()
    {
        // Default constructor for when service is not injected
    }

    public TypoDetectionHandler(INameVariantsService variantsService)
    {
        _variantsService = variantsService;
    }

    public override void Handle(NameFixContext context)
    {
        if (_variantsService == null)
            return;

        // Check primary fields
        CheckFirstName(context);
        CheckLastName(context);
        CheckMaidenName(context);

        // Check locale fields
        foreach (var locale in context.Names.Keys.ToList())
        {
            CheckLocaleNames(context, locale);
        }
    }

    private void CheckFirstName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName))
            return;

        var normalized = NormalizeName(context.FirstName);
        var canonical = FindCanonicalGivenName(normalized);

        if (canonical != null && !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            // Found a canonical form - this might be a variant or typo
            var suggestion = ToTitleCase(canonical);

            context.Changes.Add(new NameChange
            {
                Field = "FirstName",
                OldValue = context.FirstName,
                NewValue = suggestion,
                Reason = $"Possible variant/typo: '{context.FirstName}' → '{suggestion}' (canonical form)",
                Handler = Name,
                IsWarning = true  // Just a suggestion, not auto-fix
            });
        }
    }

    private void CheckLastName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LastName))
            return;

        var normalized = NormalizeName(context.LastName);
        var canonical = FindCanonicalSurname(normalized);

        if (canonical != null && !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            var suggestion = ToTitleCase(canonical);

            context.Changes.Add(new NameChange
            {
                Field = "LastName",
                OldValue = context.LastName,
                NewValue = suggestion,
                Reason = $"Possible variant/typo: '{context.LastName}' → '{suggestion}' (canonical form)",
                Handler = Name,
                IsWarning = true
            });
        }
    }

    private void CheckMaidenName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.MaidenName))
            return;

        var normalized = NormalizeName(context.MaidenName);
        var canonical = FindCanonicalSurname(normalized);

        if (canonical != null && !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            var suggestion = ToTitleCase(canonical);

            context.Changes.Add(new NameChange
            {
                Field = "MaidenName",
                OldValue = context.MaidenName,
                NewValue = suggestion,
                Reason = $"Possible variant/typo: '{context.MaidenName}' → '{suggestion}' (canonical form)",
                Handler = Name,
                IsWarning = true
            });
        }
    }

    private void CheckLocaleNames(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        // Only check en-US locale for typos (Latin names)
        if (locale != Locales.PreferredEnglish && locale != Locales.EnglishShort)
            return;

        // Check first name
        if (fields.TryGetValue(NameFields.FirstName, out var firstName) &&
            !string.IsNullOrWhiteSpace(firstName))
        {
            var normalized = NormalizeName(firstName);
            var canonical = FindCanonicalGivenName(normalized);

            if (canonical != null && !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                context.Changes.Add(new NameChange
                {
                    Field = $"{NameFields.FirstName}[{locale}]",
                    OldValue = firstName,
                    NewValue = ToTitleCase(canonical),
                    Reason = $"Possible typo detected in variants dictionary",
                    Handler = Name,
                    IsWarning = true
                });
            }
        }

        // Check last name
        if (fields.TryGetValue(NameFields.LastName, out var lastName) &&
            !string.IsNullOrWhiteSpace(lastName))
        {
            var normalized = NormalizeName(lastName);
            var canonical = FindCanonicalSurname(normalized);

            if (canonical != null && !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                context.Changes.Add(new NameChange
                {
                    Field = $"{NameFields.LastName}[{locale}]",
                    OldValue = lastName,
                    NewValue = ToTitleCase(canonical),
                    Reason = $"Possible typo detected in variants dictionary",
                    Handler = Name,
                    IsWarning = true
                });
            }
        }
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Lowercase and remove diacritics for matching
        var normalized = Transliterator.ToAscii(name).ToLowerInvariant().Trim();

        return normalized;
    }

    private string? FindCanonicalGivenName(string normalizedName)
    {
        if (_variantsService == null || string.IsNullOrEmpty(normalizedName))
            return null;

        return _variantsService.FindCanonicalGivenName(normalizedName);
    }

    private string? FindCanonicalSurname(string normalizedName)
    {
        if (_variantsService == null || string.IsNullOrEmpty(normalizedName))
            return null;

        return _variantsService.FindCanonicalSurname(normalizedName);
    }

    private static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return char.ToUpper(text[0]) + (text.Length > 1 ? text.Substring(1).ToLower() : "");
    }
}
