namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects Estonian names and moves them to the Estonian locale.
/// Estonian names often contain õ, ä, ö, ü and patterns like -mäe, -saar, -mets.
/// </summary>
public class EstonianHandler : NameFixHandlerBase
{
    public override string Name => "Estonian";
    public override int Order => 60;

    public override void Handle(NameFixContext context)
    {
        // Check all locales for Estonian names
        var localesToCheck = new[] { Locales.PreferredEnglish, Locales.EnglishShort, Locales.Russian };

        foreach (var locale in localesToCheck)
        {
            ProcessLocale(context, locale);
        }
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        // Skip if this is already the Estonian locale
        if (locale == Locales.Estonian) return;

        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Skip Cyrillic text
            if (ScriptDetector.ContainsCyrillic(value)) continue;

            // Check if this looks Estonian
            if (!ScriptDetector.IsEstonian(value)) continue;

            // Check if Estonian locale already has this field
            var existingEt = context.GetName(Locales.Estonian, field);
            if (!string.IsNullOrWhiteSpace(existingEt)) continue;

            // Copy to Estonian locale
            SetName(context, Locales.Estonian, field, value,
                $"Estonian name detected and copied from [{locale}]");
        }
    }
}
