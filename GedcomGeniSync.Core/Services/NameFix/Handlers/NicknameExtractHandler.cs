using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that extracts nicknames from first names:
/// - "Александр (Саша)" -> first_name: Александр, nicknames: Саша
/// - "Нина (Серафима)" -> first_name: Нина, nicknames: Серафима
/// - "Александр (Шура, Саша)" -> first_name: Александр, nicknames: Шура, Саша
/// - "William 'Bill'" -> first_name: William, nicknames: Bill
/// - 'Robert "Bob"' -> first_name: Robert, nicknames: Bob
///
/// Any content in parentheses within FirstName is treated as nickname(s).
/// Multiple nicknames can be comma-separated.
/// </summary>
public class NicknameExtractHandler : NameFixHandlerBase
{
    public override string Name => "NicknameExtract";
    public override int Order => 13;

    // Pattern for quoted nicknames: 'nickname' or "nickname"
    // Supports various quote styles: " ' « » „ " ' '
    private static readonly string QuoteChars = "\"'\u00AB\u00BB\u201E\u201C\u201D\u2018\u2019";
    private static readonly Regex QuotedNicknamePattern = new(
        $"[{Regex.Escape(QuoteChars)}]([^{Regex.Escape(QuoteChars)}]+)[{Regex.Escape(QuoteChars)}]",
        RegexOptions.Compiled);

    // Pattern for parenthetical nicknames in first name context
    // Note: This is different from maiden name - we check if it's a short form
    private static readonly Regex ParenNicknamePattern = new(
        @"\(([^)]+)\)",
        RegexOptions.Compiled);

    // Common nickname indicators
    private static readonly string[] NicknameIndicators = new[]
    {
        "called", "known as", "aka", "a.k.a.",
        "по прозвищу", "прозвище"
    };

    // Map of common Russian names to their diminutives (for detection)
    private static readonly Dictionary<string, HashSet<string>> RussianDiminutives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Александр"] = new() { "Саша", "Шура", "Саня", "Алекс" },
        ["Владимир"] = new() { "Володя", "Вова", "Вовка" },
        ["Дмитрий"] = new() { "Дима", "Митя", "Димон" },
        ["Михаил"] = new() { "Миша", "Мишка" },
        ["Николай"] = new() { "Коля", "Николаша" },
        ["Сергей"] = new() { "Серёжа", "Серёга" },
        ["Андрей"] = new() { "Андрюша", "Андрюха" },
        ["Иван"] = new() { "Ваня", "Ванька", "Ванюша" },
        ["Пётр"] = new() { "Петя", "Петруша" },
        ["Павел"] = new() { "Паша", "Пашка" },
        ["Мария"] = new() { "Маша", "Маруся", "Машенька" },
        ["Екатерина"] = new() { "Катя", "Катюша", "Катенька" },
        ["Анна"] = new() { "Аня", "Анечка", "Нюра" },
        ["Елена"] = new() { "Лена", "Леночка" },
        ["Ольга"] = new() { "Оля", "Оленька" },
        ["Татьяна"] = new() { "Таня", "Танюша" },
        ["Наталья"] = new() { "Наташа", "Ната" },
        ["Светлана"] = new() { "Света", "Светик" },
        ["Ирина"] = new() { "Ира", "Ирочка" },
        ["Людмила"] = new() { "Люда", "Мила", "Люся" }
    };

    private static readonly Dictionary<string, HashSet<string>> EnglishDiminutives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["William"] = new() { "Bill", "Billy", "Will", "Willy", "Liam" },
        ["Robert"] = new() { "Bob", "Bobby", "Rob", "Robbie", "Bert" },
        ["Richard"] = new() { "Dick", "Rick", "Ricky", "Rich" },
        ["Michael"] = new() { "Mike", "Micky", "Mickey" },
        ["James"] = new() { "Jim", "Jimmy", "Jamie" },
        ["John"] = new() { "Jack", "Johnny", "Jon" },
        ["Thomas"] = new() { "Tom", "Tommy" },
        ["Charles"] = new() { "Charlie", "Chuck", "Chas" },
        ["Edward"] = new() { "Ed", "Eddie", "Ted", "Teddy", "Ned" },
        ["Elizabeth"] = new() { "Liz", "Lizzy", "Beth", "Betty", "Eliza" },
        ["Margaret"] = new() { "Maggie", "Meg", "Peggy", "Marge" },
        ["Katherine"] = new() { "Kate", "Katie", "Kathy", "Kay", "Kit" },
        ["Patricia"] = new() { "Pat", "Patty", "Trish" },
        ["Jennifer"] = new() { "Jen", "Jenny", "Jenn" },
        ["Alexandra"] = new() { "Alex", "Alexa", "Lexi", "Sandra" }
    };

    public override void Handle(NameFixContext context)
    {
        var extractedNicknames = new List<string>();

        // Extract from primary FirstName
        ExtractFromPrimaryFirstName(context, extractedNicknames);

        // Extract from localized first names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ExtractFromLocale(context, locale, extractedNicknames);
        }

        // Set nicknames if any were extracted
        if (extractedNicknames.Count > 0)
        {
            var existingNicknames = context.Nicknames?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();

            var allNicknames = existingNicknames
                .Concat(extractedNicknames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var newNicknamesValue = string.Join(", ", allNicknames);

            if (newNicknamesValue != context.Nicknames)
            {
                context.Changes.Add(new NameChange
                {
                    Field = "Nicknames",
                    OldValue = context.Nicknames,
                    NewValue = newNicknamesValue,
                    Reason = $"Extracted nicknames from first name",
                    Handler = Name
                });
                context.Nicknames = newNicknamesValue;
            }
        }
    }

    private void ExtractFromPrimaryFirstName(NameFixContext context, List<string> extractedNicknames)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName)) return;

        var result = TryExtractNickname(context.FirstName);
        if (result == null) return;

        var (cleanName, nicknames) = result.Value;

        context.Changes.Add(new NameChange
        {
            Field = "FirstName",
            OldValue = context.FirstName,
            NewValue = cleanName,
            Reason = $"Extracted nickname(s) '{string.Join(", ", nicknames)}'",
            Handler = Name
        });

        context.FirstName = cleanName;
        extractedNicknames.AddRange(nicknames);
    }

    private void ExtractFromLocale(NameFixContext context, string locale, List<string> extractedNicknames)
    {
        var firstName = context.GetName(locale, NameFields.FirstName);
        if (string.IsNullOrWhiteSpace(firstName)) return;

        var result = TryExtractNickname(firstName);
        if (result == null) return;

        var (cleanName, nicknames) = result.Value;

        SetName(context, locale, NameFields.FirstName, cleanName,
            $"Extracted nickname(s) '{string.Join(", ", nicknames)}'");

        extractedNicknames.AddRange(nicknames);
    }

    /// <summary>
    /// Try to extract nicknames from a first name.
    /// Returns the clean name and list of extracted nicknames.
    /// </summary>
    private (string CleanName, List<string> Nicknames)? TryExtractNickname(string input)
    {
        var nicknames = new List<string>();
        var cleanName = input;

        // Try quoted nickname first
        var quotedMatch = QuotedNicknamePattern.Match(cleanName);
        if (quotedMatch.Success)
        {
            var nickname = quotedMatch.Groups[1].Value.Trim();
            cleanName = cleanName.Remove(quotedMatch.Index, quotedMatch.Length).Trim();
            cleanName = NormalizeSpaces(cleanName);

            if (!string.IsNullOrWhiteSpace(nickname))
            {
                nicknames.Add(nickname);
            }
        }

        // Try parenthetical nickname - extract ANY content in parentheses from FirstName
        var parenMatch = ParenNicknamePattern.Match(cleanName);
        if (parenMatch.Success)
        {
            var parenContent = parenMatch.Groups[1].Value.Trim();
            var remainingName = cleanName.Remove(parenMatch.Index, parenMatch.Length).Trim();
            remainingName = NormalizeSpaces(remainingName);

            // Only extract if we have a valid remaining name
            if (!string.IsNullOrWhiteSpace(remainingName) && !string.IsNullOrWhiteSpace(parenContent))
            {
                cleanName = remainingName;

                // Split by comma for multiple nicknames: "Шура, Саша"
                var parenNicknames = parenContent
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(n => !string.IsNullOrWhiteSpace(n));

                nicknames.AddRange(parenNicknames);
            }
        }

        if (nicknames.Count > 0 && !string.IsNullOrWhiteSpace(cleanName))
        {
            return (cleanName, nicknames);
        }

        return null;
    }

    private bool IsKnownDiminutive(string fullName, string potentialNickname)
    {
        // Check Russian diminutives
        if (RussianDiminutives.TryGetValue(fullName, out var ruDims))
        {
            if (ruDims.Contains(potentialNickname))
                return true;
        }

        // Check English diminutives
        if (EnglishDiminutives.TryGetValue(fullName, out var enDims))
        {
            if (enDims.Contains(potentialNickname))
                return true;
        }

        // Generic check: nickname should be much shorter than full name
        if (potentialNickname.Length < fullName.Length / 2 && potentialNickname.Length <= 6)
        {
            // And should share some starting letters
            if (fullName.StartsWith(potentialNickname.Substring(0, Math.Min(2, potentialNickname.Length)),
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSpaces(string input)
    {
        return Regex.Replace(input.Trim(), @"\s+", " ");
    }
}
