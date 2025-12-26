using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that removes special characters and garbage from the beginning/end of names.
/// Runs first to clean up data before other handlers process it.
/// </summary>
public class SpecialCharsCleanupHandler : NameFixHandlerBase
{
    public override string Name => "SpecialCharsCleanup";
    public override int Order => 5;

    // Characters to remove from start/end of names
    private static readonly char[] TrimChars = { '*', '?', '#', '~', '`', '^', '+', '=', '<', '>', '|', '\\', '/', '_' };

    // Regex for leading/trailing numbers (but keep Roman numerals at end for suffixes)
    private static readonly Regex LeadingNumbers = new(@"^\d+\s*", RegexOptions.Compiled);

    // Regex for multiple spaces
    private static readonly Regex MultipleSpaces = new(@"\s{2,}", RegexOptions.Compiled);

    // Characters that should not appear in names at all
    private static readonly Regex InvalidChars = new(@"[\[\]{}@#$%^&*+=<>|\\~`]", RegexOptions.Compiled);

    public override void Handle(NameFixContext context)
    {
        // Process all locales
        foreach (var locale in context.Names.Keys.ToList())
        {
            ProcessLocale(context, locale);
        }

        // Process primary fields
        ProcessPrimaryFields(context);
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.Names[locale];

        foreach (var field in NameFields.All)
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            var cleaned = CleanValue(value);
            if (cleaned != value)
            {
                SetName(context, locale, field, cleaned,
                    $"Removed special characters: '{value}' -> '{cleaned}'");
            }
        }
    }

    private void ProcessPrimaryFields(NameFixContext context)
    {
        // FirstName
        if (!string.IsNullOrWhiteSpace(context.FirstName))
        {
            var cleaned = CleanValue(context.FirstName);
            if (cleaned != context.FirstName)
            {
                var old = context.FirstName;
                context.FirstName = cleaned;
                context.Changes.Add(new NameChange
                {
                    Field = "FirstName",
                    OldValue = old,
                    NewValue = cleaned,
                    Reason = $"Removed special characters: '{old}' -> '{cleaned}'",
                    Handler = Name
                });
            }
        }

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            var cleaned = CleanValue(context.LastName);
            if (cleaned != context.LastName)
            {
                var old = context.LastName;
                context.LastName = cleaned;
                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = old,
                    NewValue = cleaned,
                    Reason = $"Removed special characters: '{old}' -> '{cleaned}'",
                    Handler = Name
                });
            }
        }

        // MiddleName
        if (!string.IsNullOrWhiteSpace(context.MiddleName))
        {
            var cleaned = CleanValue(context.MiddleName);
            if (cleaned != context.MiddleName)
            {
                var old = context.MiddleName;
                context.MiddleName = cleaned;
                context.Changes.Add(new NameChange
                {
                    Field = "MiddleName",
                    OldValue = old,
                    NewValue = cleaned,
                    Reason = $"Removed special characters: '{old}' -> '{cleaned}'",
                    Handler = Name
                });
            }
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName))
        {
            var cleaned = CleanValue(context.MaidenName);
            if (cleaned != context.MaidenName)
            {
                var old = context.MaidenName;
                context.MaidenName = cleaned;
                context.Changes.Add(new NameChange
                {
                    Field = "MaidenName",
                    OldValue = old,
                    NewValue = cleaned,
                    Reason = $"Removed special characters: '{old}' -> '{cleaned}'",
                    Handler = Name
                });
            }
        }
    }

    private static string CleanValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var result = value;

        // Remove leading/trailing special characters
        result = result.Trim(TrimChars);

        // Remove leading numbers (but not trailing - might be part of suffix)
        result = LeadingNumbers.Replace(result, "");

        // Remove invalid characters
        result = InvalidChars.Replace(result, "");

        // Normalize multiple spaces to single
        result = MultipleSpaces.Replace(result, " ");

        // Final trim
        result = result.Trim();

        return result;
    }
}
