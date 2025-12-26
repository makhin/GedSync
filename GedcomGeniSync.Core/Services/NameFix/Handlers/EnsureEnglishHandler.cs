namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that ensures English (en-US) locale is always populated with basic Latin characters.
/// This is the "required" locale that must always have valid data.
///
/// Rules:
/// 1. If en-US is empty, populate from: ru (transliterated) → other Latin locales
/// 2. If en-US contains non-basic Latin (ä, š, ł, etc.), simplify to basic Latin
/// 3. Ensure en-US contains ONLY A-Z, a-z (plus spaces, hyphens, apostrophes)
///
/// Uses Unidecode.NET library for transliteration.
/// </summary>
public class EnsureEnglishHandler : NameFixHandlerBase
{
    public override string Name => "EnsureEnglish";
    public override int Order => 35;  // After Translit (30), before FeminineSurname (40)

    public override void Handle(NameFixContext context)
    {
        // Process all name fields
        foreach (var field in NameFields.All)
        {
            EnsureEnglishField(context, field);
        }

        // Also ensure primary fields are in basic Latin
        EnsurePrimaryFieldsBasicLatin(context);
    }

    private void EnsureEnglishField(NameFixContext context, string field)
    {
        var enValue = context.GetName(Locales.PreferredEnglish, field);

        // Case 1: en-US is empty - need to populate it
        if (string.IsNullOrWhiteSpace(enValue))
        {
            PopulateEnglishField(context, field);
            enValue = context.GetName(Locales.PreferredEnglish, field);
        }

        // Case 2: en-US has non-basic Latin - simplify it
        if (!string.IsNullOrWhiteSpace(enValue) && !Transliterator.IsBasicLatin(enValue))
        {
            SimplifyToBasicLatin(context, field, enValue);
        }
    }

    private void PopulateEnglishField(NameFixContext context, string field)
    {
        // Priority 1: Transliterate from Russian
        var ruValue = context.GetName(Locales.Russian, field);
        if (!string.IsNullOrWhiteSpace(ruValue))
        {
            var transliterated = Transliterator.TransliterateCyrillic(ruValue);
            if (!string.IsNullOrWhiteSpace(transliterated))
            {
                SetName(context, Locales.PreferredEnglish, field, transliterated,
                    "Created from Russian transliteration");
                return;
            }
        }

        // Priority 2: Transliterate from Ukrainian
        var ukValue = context.GetName(Locales.Ukrainian, field);
        if (!string.IsNullOrWhiteSpace(ukValue))
        {
            var transliterated = Transliterator.TransliterateCyrillic(ukValue);
            if (!string.IsNullOrWhiteSpace(transliterated))
            {
                SetName(context, Locales.PreferredEnglish, field, transliterated,
                    "Created from Ukrainian transliteration");
                return;
            }
        }

        // Priority 3: Take from any Latin locale and simplify
        var latinLocales = new[] { Locales.Lithuanian, Locales.Estonian, Locales.Latvian,
                                   Locales.Polish, Locales.German, Locales.EnglishShort };

        foreach (var locale in latinLocales)
        {
            var value = context.GetName(locale, field);
            if (!string.IsNullOrWhiteSpace(value) && ScriptDetector.IsPurelyLatin(value))
            {
                // Simplify to basic Latin using Unidecode
                var simplified = Transliterator.RemoveDiacritics(value);
                if (!string.IsNullOrWhiteSpace(simplified))
                {
                    SetName(context, Locales.PreferredEnglish, field, simplified,
                        $"Created from [{locale}] with diacritics removed");
                    return;
                }
            }
        }

        // Priority 4: Use primary field value if available and Latin
        var primaryValue = GetPrimaryFieldValue(context, field);
        if (!string.IsNullOrWhiteSpace(primaryValue))
        {
            if (ScriptDetector.IsPurelyLatin(primaryValue))
            {
                var simplified = Transliterator.RemoveDiacritics(primaryValue);
                if (!string.IsNullOrWhiteSpace(simplified))
                {
                    SetName(context, Locales.PreferredEnglish, field, simplified,
                        "Created from primary field with diacritics removed");
                    return;
                }
            }
            else if (ScriptDetector.IsPurelyCyrillic(primaryValue))
            {
                var transliterated = Transliterator.TransliterateCyrillic(primaryValue);
                if (!string.IsNullOrWhiteSpace(transliterated))
                {
                    SetName(context, Locales.PreferredEnglish, field, transliterated,
                        "Created from primary field transliteration");
                    return;
                }
            }
        }
    }

    private void SimplifyToBasicLatin(NameFixContext context, string field, string currentValue)
    {
        // Check if it's Cyrillic in wrong place
        if (ScriptDetector.ContainsCyrillic(currentValue))
        {
            // This should have been handled by CyrillicToRuHandler
            // Just transliterate using Unidecode
            var transliterated = Transliterator.ToAscii(currentValue);
            if (!string.IsNullOrWhiteSpace(transliterated))
            {
                SetName(context, Locales.PreferredEnglish, field, transliterated,
                    "Replaced Cyrillic with transliteration");
            }
            return;
        }

        // It's Latin with diacritics - simplify using Unidecode
        var simplified = Transliterator.RemoveDiacritics(currentValue);
        if (simplified != currentValue)
        {
            SetName(context, Locales.PreferredEnglish, field, simplified,
                $"Simplified diacritics: '{currentValue}' -> '{simplified}'");
        }
    }

    private void EnsurePrimaryFieldsBasicLatin(NameFixContext context)
    {
        // FirstName
        if (!string.IsNullOrWhiteSpace(context.FirstName))
        {
            if (!Transliterator.IsBasicLatin(context.FirstName))
            {
                var old = context.FirstName;
                context.FirstName = Transliterator.ToAscii(old);

                context.Changes.Add(new NameChange
                {
                    Field = "FirstName",
                    OldValue = old,
                    NewValue = context.FirstName,
                    Reason = "Primary field simplified to basic Latin",
                    Handler = Name
                });
            }
        }

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            if (!Transliterator.IsBasicLatin(context.LastName))
            {
                var old = context.LastName;
                context.LastName = Transliterator.ToAscii(old);

                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = old,
                    NewValue = context.LastName,
                    Reason = "Primary field simplified to basic Latin",
                    Handler = Name
                });
            }
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName))
        {
            if (!Transliterator.IsBasicLatin(context.MaidenName))
            {
                var old = context.MaidenName;
                context.MaidenName = Transliterator.ToAscii(old);

                context.Changes.Add(new NameChange
                {
                    Field = "MaidenName",
                    OldValue = old,
                    NewValue = context.MaidenName,
                    Reason = "Primary field simplified to basic Latin",
                    Handler = Name
                });
            }
        }

        // MiddleName
        if (!string.IsNullOrWhiteSpace(context.MiddleName))
        {
            if (!Transliterator.IsBasicLatin(context.MiddleName))
            {
                var old = context.MiddleName;
                context.MiddleName = Transliterator.ToAscii(old);

                context.Changes.Add(new NameChange
                {
                    Field = "MiddleName",
                    OldValue = old,
                    NewValue = context.MiddleName,
                    Reason = "Primary field simplified to basic Latin",
                    Handler = Name
                });
            }
        }
    }

    private string? GetPrimaryFieldValue(NameFixContext context, string field)
    {
        return field switch
        {
            NameFields.FirstName => context.FirstName,
            NameFields.LastName => context.LastName,
            NameFields.MaidenName => context.MaidenName,
            NameFields.MiddleName => context.MiddleName,
            NameFields.Suffix => context.Suffix,
            _ => null
        };
    }
}
