namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Base class for handlers that detect a specific language/script and copy names to the appropriate locale.
/// Provides common logic for Lithuanian, Estonian, Ukrainian, Hebrew handlers.
/// </summary>
public abstract class LanguageDetectionHandlerBase : NameFixHandlerBase
{
    /// <summary>
    /// Target locale to copy detected names to (e.g., "lt", "et", "uk", "he")
    /// </summary>
    protected abstract string TargetLocale { get; }

    /// <summary>
    /// Locales to check for names in this language
    /// </summary>
    protected virtual string[] SourceLocales => new[]
    {
        Locales.PreferredEnglish,
        Locales.EnglishShort,
        Locales.Russian
    };

    /// <summary>
    /// Whether to skip Cyrillic text (for Latin-based language detection)
    /// </summary>
    protected virtual bool SkipCyrillic => true;

    /// <summary>
    /// Whether to check all locales (not just SourceLocales)
    /// </summary>
    protected virtual bool CheckAllLocales => false;

    /// <summary>
    /// Detect if the given text belongs to this language
    /// </summary>
    protected abstract bool DetectLanguage(string text);

    /// <summary>
    /// Optional: Extract only the relevant script portion from mixed text.
    /// Default implementation returns the full text.
    /// </summary>
    protected virtual string ExtractRelevantPortion(string text) => text;

    public override void Handle(NameFixContext context)
    {
        var localesToCheck = CheckAllLocales
            ? context.Names.Keys.ToList()
            : SourceLocales.ToList();

        foreach (var locale in localesToCheck)
        {
            if (locale == TargetLocale) continue;
            ProcessLocale(context, locale);
        }

        // Check primary fields
        CheckPrimaryFields(context);
    }

    protected virtual void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Skip Cyrillic if configured
            if (SkipCyrillic && ScriptDetector.ContainsCyrillic(value)) continue;

            // Check if this belongs to our language
            if (!DetectLanguage(value)) continue;

            // Check if target locale already has this field
            var existingValue = context.GetName(TargetLocale, field);
            if (!string.IsNullOrWhiteSpace(existingValue)) continue;

            // Extract relevant portion (for mixed-script text)
            var extracted = ExtractRelevantPortion(value);
            if (string.IsNullOrWhiteSpace(extracted)) continue;

            // Copy to target locale
            SetName(context, TargetLocale, field, extracted,
                $"{Name} name detected and copied from [{locale}]");
        }
    }

    protected virtual void CheckPrimaryFields(NameFixContext context)
    {
        ProcessPrimaryField(context, context.FirstName, NameFields.FirstName);
        ProcessPrimaryField(context, context.LastName, NameFields.LastName);
        ProcessPrimaryField(context, context.MiddleName, NameFields.MiddleName);
        ProcessPrimaryField(context, context.MaidenName, NameFields.MaidenName);
    }

    private void ProcessPrimaryField(NameFixContext context, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (SkipCyrillic && ScriptDetector.ContainsCyrillic(value)) return;
        if (!DetectLanguage(value)) return;

        var existingValue = context.GetName(TargetLocale, field);
        if (!string.IsNullOrWhiteSpace(existingValue)) return;

        var extracted = ExtractRelevantPortion(value);
        if (string.IsNullOrWhiteSpace(extracted)) return;

        SetName(context, TargetLocale, field, extracted,
            $"{Name} name detected from primary field");
    }
}
