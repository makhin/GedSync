namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that generates transliterations for names.
/// If Russian names exist but English doesn't, creates Latin transliteration.
/// Uses Unidecode.NET library for transliteration.
/// </summary>
public class TranslitHandler : NameFixHandlerBase
{
    public override string Name => "Translit";
    public override int Order => 30;

    public override void Handle(NameFixContext context)
    {
        // Generate English transliterations from Russian
        GenerateTransliterations(context, Locales.Russian, Locales.PreferredEnglish);

        // Also generate transliterations for Ukrainian if present
        GenerateTransliterations(context, Locales.Ukrainian, Locales.PreferredEnglish);

        // Update primary fields if they're Cyrillic and we have transliterations
        UpdatePrimaryFields(context);
    }

    private void GenerateTransliterations(NameFixContext context, string sourceLocale, string targetLocale)
    {
        var sourceFields = context.GetLocaleFields(sourceLocale);
        if (sourceFields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!sourceFields.TryGetValue(field, out var sourceValue)) continue;
            if (string.IsNullOrWhiteSpace(sourceValue)) continue;

            // Check if target locale already has this field
            var targetValue = context.GetName(targetLocale, field);
            if (!string.IsNullOrWhiteSpace(targetValue)) continue;

            // Check if source is Cyrillic
            if (!ScriptDetector.ContainsCyrillic(sourceValue)) continue;

            // Generate transliteration using Unidecode.NET
            var transliterated = Transliterator.TransliterateCyrillic(sourceValue);
            if (string.IsNullOrWhiteSpace(transliterated)) continue;

            SetName(context, targetLocale, field, transliterated,
                $"Transliterated from {sourceLocale}");
        }
    }

    private void UpdatePrimaryFields(NameFixContext context)
    {
        // If primary fields are Cyrillic and we have English transliterations,
        // consider updating the primary fields to Latin

        // FirstName
        if (!string.IsNullOrWhiteSpace(context.FirstName) &&
            ScriptDetector.IsPurelyCyrillic(context.FirstName))
        {
            var enValue = context.GetName(Locales.PreferredEnglish, NameFields.FirstName);
            if (!string.IsNullOrWhiteSpace(enValue) && ScriptDetector.IsPurelyLatin(enValue))
            {
                UpdatePrimaryField(context, "FirstName", context.FirstName, enValue,
                    v => context.FirstName = v);
            }
            else
            {
                // Generate transliteration for primary field using Unidecode.NET
                var translit = Transliterator.TransliterateCyrillic(context.FirstName);
                if (!string.IsNullOrWhiteSpace(translit))
                {
                    UpdatePrimaryField(context, "FirstName", context.FirstName, translit,
                        v => context.FirstName = v);
                }
            }
        }

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName) &&
            ScriptDetector.IsPurelyCyrillic(context.LastName))
        {
            var enValue = context.GetName(Locales.PreferredEnglish, NameFields.LastName);
            if (!string.IsNullOrWhiteSpace(enValue) && ScriptDetector.IsPurelyLatin(enValue))
            {
                UpdatePrimaryField(context, "LastName", context.LastName, enValue,
                    v => context.LastName = v);
            }
            else
            {
                var translit = Transliterator.TransliterateCyrillic(context.LastName);
                if (!string.IsNullOrWhiteSpace(translit))
                {
                    UpdatePrimaryField(context, "LastName", context.LastName, translit,
                        v => context.LastName = v);
                }
            }
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName) &&
            ScriptDetector.IsPurelyCyrillic(context.MaidenName))
        {
            var enValue = context.GetName(Locales.PreferredEnglish, NameFields.MaidenName);
            if (!string.IsNullOrWhiteSpace(enValue) && ScriptDetector.IsPurelyLatin(enValue))
            {
                UpdatePrimaryField(context, "MaidenName", context.MaidenName, enValue,
                    v => context.MaidenName = v);
            }
            else
            {
                var translit = Transliterator.TransliterateCyrillic(context.MaidenName);
                if (!string.IsNullOrWhiteSpace(translit))
                {
                    UpdatePrimaryField(context, "MaidenName", context.MaidenName, translit,
                        v => context.MaidenName = v);
                }
            }
        }

        // MiddleName
        if (!string.IsNullOrWhiteSpace(context.MiddleName) &&
            ScriptDetector.IsPurelyCyrillic(context.MiddleName))
        {
            var enValue = context.GetName(Locales.PreferredEnglish, NameFields.MiddleName);
            if (!string.IsNullOrWhiteSpace(enValue) && ScriptDetector.IsPurelyLatin(enValue))
            {
                UpdatePrimaryField(context, "MiddleName", context.MiddleName, enValue,
                    v => context.MiddleName = v);
            }
            else
            {
                var translit = Transliterator.TransliterateCyrillic(context.MiddleName);
                if (!string.IsNullOrWhiteSpace(translit))
                {
                    UpdatePrimaryField(context, "MiddleName", context.MiddleName, translit,
                        v => context.MiddleName = v);
                }
            }
        }
    }

    private void UpdatePrimaryField(NameFixContext context, string fieldName, string oldValue, string newValue, Action<string> setter)
    {
        setter(newValue);
        context.Changes.Add(new NameChange
        {
            Field = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = "Primary field updated to Latin transliteration",
            Handler = Name
        });
    }
}
