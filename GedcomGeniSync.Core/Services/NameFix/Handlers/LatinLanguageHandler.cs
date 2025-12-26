namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects other Latin-script languages (Latvian, Polish, German)
/// and copies names to appropriate locales.
/// </summary>
public class LatinLanguageHandler : NameFixHandlerBase
{
    public override string Name => "LatinLanguage";
    public override int Order => 27;  // Before EnsureEnglish (35) to preserve original with diacritics

    public override void Handle(NameFixContext context)
    {
        // Check English locales for non-English Latin names
        ProcessLocale(context, Locales.PreferredEnglish);
        ProcessLocale(context, Locales.EnglishShort);
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Skip Cyrillic text
            if (ScriptDetector.ContainsCyrillic(value)) continue;

            // Try to detect specific language
            var detection = ScriptDetector.DetectLatinLanguage(value);
            if (detection == null) continue;
            if (detection.Confidence < 0.75) continue;

            // Skip if detected as English
            if (Locales.IsEnglish(detection.LanguageCode)) continue;

            // Skip Lithuanian and Estonian (handled by dedicated handlers)
            if (detection.LanguageCode == Locales.Lithuanian) continue;
            if (detection.LanguageCode == Locales.Estonian) continue;

            // Check if target locale already has this field
            var existingValue = context.GetName(detection.LanguageCode, field);
            if (!string.IsNullOrWhiteSpace(existingValue)) continue;

            // Copy to detected locale
            SetName(context, detection.LanguageCode, field, value,
                $"{detection.Reason} - copied from [{locale}] to [{detection.LanguageCode}]");
        }
    }
}
