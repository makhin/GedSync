namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that moves Cyrillic names from English locale to Russian locale.
/// Detects when Cyrillic text is incorrectly stored in en/en-US and moves it to ru.
/// </summary>
public class CyrillicToRuHandler : NameFixHandlerBase
{
    public override string Name => "CyrillicToRu";
    public override int Order => 20;

    public override void Handle(NameFixContext context)
    {
        // Process English locales
        ProcessEnglishLocale(context, Locales.English);
        ProcessEnglishLocale(context, Locales.EnglishShort);

        // Also check primary name fields
        ProcessPrimaryNames(context);
    }

    private void ProcessEnglishLocale(NameFixContext context, string locale)
    {
        if (!context.Names.TryGetValue(locale, out var fields)) return;

        // Get list of fields to process (avoid modifying collection during iteration)
        var fieldsToProcess = fields.Keys.ToList();

        foreach (var field in fieldsToProcess)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Check if value is purely Cyrillic
            if (!ScriptDetector.IsPurelyCyrillic(value)) continue;

            // Check if Russian locale already has this field
            var existingRu = context.GetName(Locales.Russian, field);

            if (string.IsNullOrWhiteSpace(existingRu))
            {
                // Move to Russian
                MoveName(context, locale, Locales.Russian, field,
                    "Cyrillic text moved from English to Russian locale");
            }
            else if (existingRu.Equals(value, StringComparison.Ordinal))
            {
                // Same value exists in Russian, just remove from English
                SetName(context, locale, field, null,
                    "Removed duplicate Cyrillic text (already in ru)");
            }
            else
            {
                // Different value in Russian - log but don't overwrite
                // Just clear the English locale
                SetName(context, locale, field, null,
                    $"Removed Cyrillic text from English (ru has different value: '{existingRu}')");
            }
        }
    }

    private void ProcessPrimaryNames(NameFixContext context)
    {
        // Check primary fields for Cyrillic content
        // These should ideally be in Latin for the primary (display) fields

        // FirstName
        if (!string.IsNullOrWhiteSpace(context.FirstName) &&
            ScriptDetector.IsPurelyCyrillic(context.FirstName))
        {
            EnsureCyrillicInRuLocale(context, NameFields.FirstName, context.FirstName);
        }

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName) &&
            ScriptDetector.IsPurelyCyrillic(context.LastName))
        {
            EnsureCyrillicInRuLocale(context, NameFields.LastName, context.LastName);
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName) &&
            ScriptDetector.IsPurelyCyrillic(context.MaidenName))
        {
            EnsureCyrillicInRuLocale(context, NameFields.MaidenName, context.MaidenName);
        }

        // MiddleName
        if (!string.IsNullOrWhiteSpace(context.MiddleName) &&
            ScriptDetector.IsPurelyCyrillic(context.MiddleName))
        {
            EnsureCyrillicInRuLocale(context, NameFields.MiddleName, context.MiddleName);
        }
    }

    private void EnsureCyrillicInRuLocale(NameFixContext context, string field, string cyrillicValue)
    {
        var existingRu = context.GetName(Locales.Russian, field);

        if (string.IsNullOrWhiteSpace(existingRu))
        {
            // Copy to Russian locale
            SetName(context, Locales.Russian, field, cyrillicValue,
                "Copied Cyrillic value from primary field to ru locale");
        }
        // If ru already has a value, we don't overwrite it
    }
}
