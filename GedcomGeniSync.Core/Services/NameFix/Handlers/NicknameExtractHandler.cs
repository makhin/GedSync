using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that extracts nicknames from names:
/// - "Александр 'Саша' Петров" -> first_name: Александр, nickname: Саша
/// - "William (Bill) Smith" -> first_name: William, nickname: Bill
/// - 'Robert "Bob" Johnson' -> first_name: Robert, nickname: Bob
/// </summary>
public class NicknameExtractHandler : NameFixHandlerBase
{
    public override string Name => "NicknameExtract";
    public override int Order => 13;

    // Pattern for quoted nicknames: 'nickname' or "nickname"
    // Supports: " ' « » „ " ' '
    private static readonly Regex QuotedNicknamePattern = new(
        "[\"'«»„"''']([^\"'«»„"''']+)[\"'«»„"''']",
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
        // Extract from primary FirstName
        ExtractFromPrimaryFirstName(context);

        // Extract from localized first names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ExtractFromLocale(context, locale);
        }
    }

    private void ExtractFromPrimaryFirstName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName)) return;

        var result = TryExtractNickname(context.FirstName);
        if (result == null) return;

        var (cleanName, nickname) = result.Value;

        context.Changes.Add(new NameChange
        {
            Field = "FirstName",
            OldValue = context.FirstName,
            NewValue = cleanName,
            Reason = $"Extracted nickname '{nickname}'",
            Handler = Name
        });

        context.FirstName = cleanName;
        // Note: Would set context.Nickname if it existed
    }

    private void ExtractFromLocale(NameFixContext context, string locale)
    {
        var firstName = context.GetName(locale, NameFields.FirstName);
        if (string.IsNullOrWhiteSpace(firstName)) return;

        var result = TryExtractNickname(firstName);
        if (result == null) return;

        var (cleanName, nickname) = result.Value;

        SetName(context, locale, NameFields.FirstName, cleanName,
            $"Extracted nickname '{nickname}'");

        // Store nickname - Geni uses "nicknames" field
        // For now, we just clean the first name
    }

    private (string CleanName, string Nickname)? TryExtractNickname(string input)
    {
        // Try quoted nickname first
        var quotedMatch = QuotedNicknamePattern.Match(input);
        if (quotedMatch.Success)
        {
            var nickname = quotedMatch.Groups[1].Value.Trim();
            var cleanName = input.Remove(quotedMatch.Index, quotedMatch.Length).Trim();
            cleanName = NormalizeSpaces(cleanName);

            if (!string.IsNullOrWhiteSpace(nickname) && !string.IsNullOrWhiteSpace(cleanName))
            {
                return (cleanName, nickname);
            }
        }

        // Try parenthetical nickname (only if it looks like a diminutive, not maiden name)
        var parenMatch = ParenNicknamePattern.Match(input);
        if (parenMatch.Success)
        {
            var potentialNickname = parenMatch.Groups[1].Value.Trim();
            var remainingName = input.Remove(parenMatch.Index, parenMatch.Length).Trim();
            remainingName = NormalizeSpaces(remainingName);

            // Check if it's a known diminutive
            if (IsKnownDiminutive(remainingName, potentialNickname))
            {
                return (remainingName, potentialNickname);
            }
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
