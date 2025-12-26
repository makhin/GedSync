using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for Russian/Slavic patronymics (отчества).
/// Handles cases where:
/// - Full name is in one field: "Иван Петрович Сидоров" -> split correctly
/// - Patronymic is in wrong field (e.g., in last_name)
/// - Patronymic should be moved to middle_name
/// </summary>
public class PatronymicHandler : NameFixHandlerBase
{
    public override string Name => "Patronymic";
    public override int Order => 15;

    // Russian patronymic endings
    private static readonly string[] MalePatronymicEndings = new[]
    {
        "ович", "евич", "ёвич", "ич"  // Петрович, Сергеевич, Львович, Ильич
    };

    private static readonly string[] FemalePatronymicEndings = new[]
    {
        "овна", "евна", "ёвна", "ична", "инична"  // Петровна, Сергеевна, Никитична
    };

    // Pattern to match "FirstName Patronymic LastName" in a single field
    private static readonly Regex FullNamePattern = new(
        @"^(\S+)\s+(\S+(?:ович|евич|ёвич|ич|овна|евна|ёвна|ична|инична))\s+(\S+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to match "FirstName Patronymic" (without last name)
    private static readonly Regex FirstAndPatronymicPattern = new(
        @"^(\S+)\s+(\S+(?:ович|евич|ёвич|ич|овна|евна|ёвна|ична|инична))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override void Handle(NameFixContext context)
    {
        // Check if FirstName contains full name with patronymic
        TrySplitFullName(context);

        // Check if LastName is actually a patronymic
        CheckLastNameForPatronymic(context);

        // Process localized names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ProcessLocale(context, locale);
        }
    }

    private void TrySplitFullName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName)) return;

        // Check for "FirstName Patronymic LastName" pattern
        var fullMatch = FullNamePattern.Match(context.FirstName);
        if (fullMatch.Success)
        {
            var firstName = fullMatch.Groups[1].Value;
            var patronymic = fullMatch.Groups[2].Value;
            var lastName = fullMatch.Groups[3].Value;

            // Only split if other fields are empty
            if (string.IsNullOrWhiteSpace(context.MiddleName) &&
                string.IsNullOrWhiteSpace(context.LastName))
            {
                context.Changes.Add(new NameChange
                {
                    Field = "FirstName",
                    OldValue = context.FirstName,
                    NewValue = firstName,
                    Reason = "Split full name into components",
                    Handler = Name
                });

                context.FirstName = firstName;
                context.MiddleName = patronymic;
                context.LastName = lastName;

                context.Changes.Add(new NameChange
                {
                    Field = "MiddleName",
                    OldValue = null,
                    NewValue = patronymic,
                    Reason = "Patronymic extracted from full name",
                    Handler = Name
                });

                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = null,
                    NewValue = lastName,
                    Reason = "Last name extracted from full name",
                    Handler = Name
                });

                return;
            }
        }

        // Check for "FirstName Patronymic" pattern (no last name)
        var partialMatch = FirstAndPatronymicPattern.Match(context.FirstName);
        if (partialMatch.Success && string.IsNullOrWhiteSpace(context.MiddleName))
        {
            var firstName = partialMatch.Groups[1].Value;
            var patronymic = partialMatch.Groups[2].Value;

            context.Changes.Add(new NameChange
            {
                Field = "FirstName",
                OldValue = context.FirstName,
                NewValue = firstName,
                Reason = "Split patronymic from first name",
                Handler = Name
            });

            context.FirstName = firstName;
            context.MiddleName = patronymic;

            context.Changes.Add(new NameChange
            {
                Field = "MiddleName",
                OldValue = null,
                NewValue = patronymic,
                Reason = "Patronymic extracted from first name",
                Handler = Name
            });
        }
    }

    private void CheckLastNameForPatronymic(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LastName)) return;

        // Check if LastName is actually a patronymic
        if (IsPatronymic(context.LastName))
        {
            // Only move if MiddleName is empty
            if (string.IsNullOrWhiteSpace(context.MiddleName))
            {
                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = context.LastName,
                    NewValue = null,
                    Reason = "Moved patronymic to middle name",
                    Handler = Name
                });

                context.MiddleName = context.LastName;
                context.LastName = null;

                context.Changes.Add(new NameChange
                {
                    Field = "MiddleName",
                    OldValue = null,
                    NewValue = context.MiddleName,
                    Reason = "Patronymic moved from last name",
                    Handler = Name
                });
            }
        }
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        // Only process Russian/Ukrainian locales where patronymics are used
        if (locale != Locales.Russian && locale != Locales.Ukrainian)
            return;

        var firstName = context.GetName(locale, NameFields.FirstName);
        if (string.IsNullOrWhiteSpace(firstName)) return;

        // Check for full name pattern
        var fullMatch = FullNamePattern.Match(firstName);
        if (fullMatch.Success)
        {
            var existingMiddle = context.GetName(locale, NameFields.MiddleName);
            var existingLast = context.GetName(locale, NameFields.LastName);

            if (string.IsNullOrWhiteSpace(existingMiddle) && string.IsNullOrWhiteSpace(existingLast))
            {
                SetName(context, locale, NameFields.FirstName, fullMatch.Groups[1].Value,
                    "Split full name");
                SetName(context, locale, NameFields.MiddleName, fullMatch.Groups[2].Value,
                    "Patronymic extracted");
                SetName(context, locale, NameFields.LastName, fullMatch.Groups[3].Value,
                    "Last name extracted");
            }
        }

        // Check last_name for patronymic
        var lastName = context.GetName(locale, NameFields.LastName);
        if (!string.IsNullOrWhiteSpace(lastName) && IsPatronymic(lastName))
        {
            var existingMiddle = context.GetName(locale, NameFields.MiddleName);
            if (string.IsNullOrWhiteSpace(existingMiddle))
            {
                SetName(context, locale, NameFields.MiddleName, lastName,
                    "Patronymic moved from last_name");
                SetName(context, locale, NameFields.LastName, null,
                    "Cleared - was actually a patronymic");
            }
        }
    }

    private bool IsPatronymic(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var lower = value.ToLowerInvariant();

        // Check male endings
        foreach (var ending in MalePatronymicEndings)
        {
            if (lower.EndsWith(ending) && value.Length > ending.Length + 2)
                return true;
        }

        // Check female endings
        foreach (var ending in FemalePatronymicEndings)
        {
            if (lower.EndsWith(ending) && value.Length > ending.Length + 2)
                return true;
        }

        return false;
    }
}
