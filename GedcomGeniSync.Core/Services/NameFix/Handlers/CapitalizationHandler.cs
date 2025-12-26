namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for fixing capitalization issues.
/// Handles:
/// - ALL CAPS names → Proper Case
/// - all lowercase names → Proper Case
/// - Hyphenated names: ANNA-MARIA → Anna-Maria
///
/// Note: Special surname particles (Mc, Mac, O', von, van) are handled by SurnameParticleHandler.
/// </summary>
public class CapitalizationHandler : NameFixHandlerBase
{
    public override string Name => "Capitalization";
    public override int Order => 95;

    public override void Handle(NameFixContext context)
    {
        // Process primary fields
        ProcessPrimaryField(context, context.FirstName, "FirstName", v => context.FirstName = v);
        ProcessPrimaryField(context, context.LastName, "LastName", v => context.LastName = v);
        ProcessPrimaryField(context, context.MiddleName, "MiddleName", v => context.MiddleName = v);
        ProcessPrimaryField(context, context.MaidenName, "MaidenName", v => context.MaidenName = v);

        // Process all locales
        foreach (var locale in context.Names.Keys.ToList())
        {
            ProcessLocale(context, locale);
        }
    }

    private void ProcessPrimaryField(NameFixContext context, string? value, string fieldName, Action<string> setter)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!NeedsCapitalizationFix(value)) return;

        var fixed_ = FixCapitalization(value);
        if (fixed_ == value) return;

        context.Changes.Add(new NameChange
        {
            Field = fieldName,
            OldValue = value,
            NewValue = fixed_,
            Reason = "Fixed capitalization",
            Handler = Name
        });
        setter(fixed_);
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!NeedsCapitalizationFix(value)) continue;

            var fixed_ = FixCapitalization(value);
            if (fixed_ != value)
            {
                SetName(context, locale, field, fixed_, "Fixed capitalization");
            }
        }
    }

    /// <summary>
    /// Check if text needs capitalization fix (all caps or all lower)
    /// </summary>
    private static bool NeedsCapitalizationFix(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return false;

        // All uppercase
        if (letters.All(char.IsUpper)) return true;

        // All lowercase
        if (letters.All(char.IsLower)) return true;

        return false;
    }

    private static string FixCapitalization(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(CapitalizeWord));
    }

    private static string CapitalizeWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Handle hyphenated names (Anna-Maria)
        if (word.Contains('-'))
        {
            var parts = word.Split('-');
            return string.Join("-", parts.Select(CapitalizeWord));
        }

        // Handle apostrophe (O'Brien)
        var apostropheIndex = word.IndexOf('\'');
        if (apostropheIndex > 0 && apostropheIndex < word.Length - 1)
        {
            var before = word.Substring(0, apostropheIndex + 1);
            var after = word.Substring(apostropheIndex + 1);
            return char.ToUpper(before[0]) + before.Substring(1).ToLower() +
                   char.ToUpper(after[0]) + after.Substring(1).ToLower();
        }

        return char.ToUpper(word[0]) + (word.Length > 1 ? word.Substring(1).ToLower() : "");
    }
}
