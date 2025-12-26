namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that splits mixed-script names into separate locales.
/// For example: "Петров Petrov" -> "Петров" (ru) + "Petrov" (en)
/// This runs first in the pipeline to prepare data for other handlers.
/// </summary>
public class ScriptSplitHandler : NameFixHandlerBase
{
    public override string Name => "ScriptSplit";
    public override int Order => 10;

    public override void Handle(NameFixContext context)
    {
        // Process all locales looking for mixed-script content
        var localesToProcess = context.Names.Keys.ToList();

        foreach (var locale in localesToProcess)
        {
            ProcessLocale(context, locale);
        }

        // Also check primary name fields
        ProcessPrimaryNames(context);
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.Names.GetValueOrDefault(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            var script = ScriptDetector.DetectScript(value);
            if (script != ScriptDetector.TextScript.Mixed) continue;

            // Split the mixed-script value
            SplitMixedValue(context, locale, field, value);
        }
    }

    private void SplitMixedValue(NameFixContext context, string locale, string field, string value)
    {
        var parts = ScriptDetector.SplitByScript(value);
        if (parts.Count <= 1) return; // Nothing to split

        // Get the Cyrillic part
        if (parts.TryGetValue(ScriptDetector.TextScript.Cyrillic, out var cyrillicPart) &&
            !string.IsNullOrWhiteSpace(cyrillicPart))
        {
            // Put Cyrillic in Russian locale
            var existingRu = context.GetName(Locales.Russian, field);
            if (string.IsNullOrWhiteSpace(existingRu))
            {
                SetName(context, Locales.Russian, field, cyrillicPart.Trim(),
                    $"Split from mixed-script value in [{locale}]");
            }
        }

        // Get the Latin part
        if (parts.TryGetValue(ScriptDetector.TextScript.Latin, out var latinPart) &&
            !string.IsNullOrWhiteSpace(latinPart))
        {
            // Determine the best locale for Latin text
            var latinLocale = DetermineLatinLocale(latinPart, locale);

            // Update the original locale with just the Latin part (if it's English)
            // or move to appropriate locale
            if (Locales.IsEnglish(locale))
            {
                SetName(context, locale, field, latinPart.Trim(),
                    "Removed Cyrillic portion, kept Latin only");
            }
            else
            {
                // Put Latin in English locale
                var existingEn = context.GetName(Locales.PreferredEnglish, field);
                if (string.IsNullOrWhiteSpace(existingEn))
                {
                    SetName(context, Locales.PreferredEnglish, field, latinPart.Trim(),
                        $"Split from mixed-script value in [{locale}]");
                }

                // Clear the original if it only contained this mixed value
                SetName(context, locale, field, null,
                    "Cleared after splitting mixed-script value");
            }
        }

        // Get the Hebrew part if any
        if (parts.TryGetValue(ScriptDetector.TextScript.Hebrew, out var hebrewPart) &&
            !string.IsNullOrWhiteSpace(hebrewPart))
        {
            var existingHe = context.GetName(Locales.Hebrew, field);
            if (string.IsNullOrWhiteSpace(existingHe))
            {
                SetName(context, Locales.Hebrew, field, hebrewPart.Trim(),
                    $"Split from mixed-script value in [{locale}]");
            }
        }
    }

    private void ProcessPrimaryNames(NameFixContext context)
    {
        // Check if primary name fields contain mixed scripts
        ProcessPrimaryField(context, nameof(context.FirstName), context.FirstName, v => context.FirstName = v);
        ProcessPrimaryField(context, nameof(context.LastName), context.LastName, v => context.LastName = v);
        ProcessPrimaryField(context, nameof(context.MaidenName), context.MaidenName, v => context.MaidenName = v);
        ProcessPrimaryField(context, nameof(context.MiddleName), context.MiddleName, v => context.MiddleName = v);
    }

    private void ProcessPrimaryField(NameFixContext context, string fieldName, string? value, Action<string?> setter)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var script = ScriptDetector.DetectScript(value);
        if (script != ScriptDetector.TextScript.Mixed) return;

        var parts = ScriptDetector.SplitByScript(value);
        if (parts.Count <= 1) return;

        // Get the appropriate Geni field name
        var geniField = fieldName switch
        {
            nameof(NameFixContext.FirstName) => NameFields.FirstName,
            nameof(NameFixContext.LastName) => NameFields.LastName,
            nameof(NameFixContext.MaidenName) => NameFields.MaidenName,
            nameof(NameFixContext.MiddleName) => NameFields.MiddleName,
            _ => null
        };

        if (geniField == null) return;

        // Split into appropriate locales
        if (parts.TryGetValue(ScriptDetector.TextScript.Cyrillic, out var cyrillicPart) &&
            !string.IsNullOrWhiteSpace(cyrillicPart))
        {
            var existingRu = context.GetName(Locales.Russian, geniField);
            if (string.IsNullOrWhiteSpace(existingRu))
            {
                SetName(context, Locales.Russian, geniField, cyrillicPart.Trim(),
                    $"Split from mixed-script primary {fieldName}");
            }
        }

        if (parts.TryGetValue(ScriptDetector.TextScript.Latin, out var latinPart) &&
            !string.IsNullOrWhiteSpace(latinPart))
        {
            var existingEn = context.GetName(Locales.PreferredEnglish, geniField);
            if (string.IsNullOrWhiteSpace(existingEn))
            {
                SetName(context, Locales.PreferredEnglish, geniField, latinPart.Trim(),
                    $"Split from mixed-script primary {fieldName}");
            }

            // Update primary field to Latin only
            setter(latinPart.Trim());
            context.Changes.Add(new NameChange
            {
                Field = fieldName,
                OldValue = value,
                NewValue = latinPart.Trim(),
                Reason = "Primary field: kept Latin, moved Cyrillic to ru locale",
                Handler = Name
            });
        }
    }

    private string DetermineLatinLocale(string latinText, string originalLocale)
    {
        // Try to detect specific language
        var detection = ScriptDetector.DetectLatinLanguage(latinText);
        if (detection != null && detection.Confidence >= 0.8)
        {
            return detection.LanguageCode;
        }

        // If original locale was English, keep it there
        if (Locales.IsEnglish(originalLocale))
        {
            return originalLocale;
        }

        // Default to English for Latin text
        return Locales.PreferredEnglish;
    }
}
