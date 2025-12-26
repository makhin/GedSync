namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects Lithuanian names and moves them to the Lithuanian locale.
/// Lithuanian names have distinctive patterns: ą, č, ę, ė, etc. and endings like -auskas, -aitis.
/// </summary>
public class LithuanianHandler : NameFixHandlerBase
{
    public override string Name => "Lithuanian";
    public override int Order => 50;

    public override void Handle(NameFixContext context)
    {
        // Check all locales for Lithuanian names
        var localesToCheck = new[] { Locales.PreferredEnglish, Locales.EnglishShort, Locales.Russian };

        foreach (var locale in localesToCheck)
        {
            ProcessLocale(context, locale);
        }
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        // Skip if this is already the Lithuanian locale
        if (locale == Locales.Lithuanian) return;

        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Skip Cyrillic text (should be handled by Russian handler)
            if (ScriptDetector.ContainsCyrillic(value)) continue;

            // Check if this looks Lithuanian
            if (!ScriptDetector.IsLithuanian(value)) continue;

            // Check if Lithuanian locale already has this field
            var existingLt = context.GetName(Locales.Lithuanian, field);
            if (!string.IsNullOrWhiteSpace(existingLt)) continue;

            // Copy to Lithuanian locale (don't remove from original - might be needed for display)
            SetName(context, Locales.Lithuanian, field, value,
                $"Lithuanian name detected and copied from [{locale}]");
        }
    }
}
