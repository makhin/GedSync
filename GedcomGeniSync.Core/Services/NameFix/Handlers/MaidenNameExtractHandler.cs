using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that extracts maiden names from various formats:
/// - "Иванова (Петрова)" -> last_name: Иванова, maiden_name: Петрова
/// - "Ivanova née Petrova" -> last_name: Ivanova, maiden_name: Petrova
/// - "Иванова (урожд. Петрова)" -> last_name: Иванова, maiden_name: Петрова
/// - "Smith (born Jones)" -> last_name: Smith, maiden_name: Jones
/// </summary>
public class MaidenNameExtractHandler : NameFixHandlerBase
{
    public override string Name => "MaidenNameExtract";
    public override int Order => 12;

    // Patterns for maiden name extraction
    // Pattern 1: Simple parentheses - "Иванова (Петрова)"
    private static readonly Regex ParenthesesPattern = new(
        @"^(.+?)\s*\(([^)]+)\)\s*$",
        RegexOptions.Compiled);

    // Pattern 2: "née" or "nee" - "Ivanova née Petrova"
    private static readonly Regex NeePattern = new(
        @"^(.+?)\s+n[eé]e\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern 3: "born" - "Smith born Jones"
    private static readonly Regex BornPattern = new(
        @"^(.+?)\s+born\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern 4: Russian "урожд." or "урождённая" - "Иванова (урожд. Петрова)"
    private static readonly Regex RussianMaidenPattern = new(
        @"^(.+?)\s*\(\s*урожд(?:ённая|\.)\s*(.+?)\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern 5: Slash separator - "Ivanova/Petrova"
    private static readonly Regex SlashPattern = new(
        @"^(.+?)\s*/\s*(.+)$",
        RegexOptions.Compiled);

    // Words that indicate it's not a maiden name in parentheses
    private static readonly HashSet<string> NonMaidenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // These in parentheses are usually nicknames or notes, not maiden names
        "aka", "a.k.a.", "alias", "called", "known as",
        "deceased", "dead", "died", "умер", "умерла",
        "infant", "child", "baby", "младенец", "ребёнок",
        "unknown", "неизвестн"
    };

    public override void Handle(NameFixContext context)
    {
        // Only process if maiden_name is empty (don't overwrite existing)
        if (!string.IsNullOrWhiteSpace(context.MaidenName)) return;

        // Check primary LastName
        ExtractFromPrimaryLastName(context);

        // Check localized last names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ExtractFromLocale(context, locale);
        }
    }

    private void ExtractFromPrimaryLastName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LastName)) return;
        if (!string.IsNullOrWhiteSpace(context.MaidenName)) return;

        var result = TryExtractMaidenName(context.LastName);
        if (result == null) return;

        var (lastName, maidenName) = result.Value;

        context.Changes.Add(new NameChange
        {
            Field = "LastName",
            OldValue = context.LastName,
            NewValue = lastName,
            Reason = $"Extracted maiden name '{maidenName}'",
            Handler = Name
        });

        context.LastName = lastName;
        context.MaidenName = maidenName;

        context.Changes.Add(new NameChange
        {
            Field = "MaidenName",
            OldValue = null,
            NewValue = maidenName,
            Reason = "Maiden name extracted from last name",
            Handler = Name
        });
    }

    private void ExtractFromLocale(NameFixContext context, string locale)
    {
        var lastName = context.GetName(locale, NameFields.LastName);
        if (string.IsNullOrWhiteSpace(lastName)) return;

        var existingMaiden = context.GetName(locale, NameFields.MaidenName);
        if (!string.IsNullOrWhiteSpace(existingMaiden)) return;

        var result = TryExtractMaidenName(lastName);
        if (result == null) return;

        var (newLastName, maidenName) = result.Value;

        SetName(context, locale, NameFields.LastName, newLastName,
            $"Extracted maiden name '{maidenName}'");
        SetName(context, locale, NameFields.MaidenName, maidenName,
            "Maiden name extracted from last name");
    }

    private (string LastName, string MaidenName)? TryExtractMaidenName(string input)
    {
        // Try Russian pattern first (most specific)
        var match = RussianMaidenPattern.Match(input);
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }

        // Try "née" pattern
        match = NeePattern.Match(input);
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }

        // Try "born" pattern
        match = BornPattern.Match(input);
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }

        // Try simple parentheses (but check for non-maiden keywords)
        match = ParenthesesPattern.Match(input);
        if (match.Success)
        {
            var potentialMaiden = match.Groups[2].Value.Trim();

            // Check if this looks like a maiden name (not a note/nickname)
            if (!IsLikelyMaidenName(potentialMaiden))
                return null;

            return (match.Groups[1].Value.Trim(), potentialMaiden);
        }

        // Try slash pattern (less reliable - only if both parts look like surnames)
        match = SlashPattern.Match(input);
        if (match.Success)
        {
            var part1 = match.Groups[1].Value.Trim();
            var part2 = match.Groups[2].Value.Trim();

            // Only use if both look like surnames (single words, capitalized)
            if (LooksLikeSurname(part1) && LooksLikeSurname(part2))
            {
                return (part1, part2);
            }
        }

        return null;
    }

    private bool IsLikelyMaidenName(string value)
    {
        // Check for non-maiden keywords
        foreach (var keyword in NonMaidenKeywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // A maiden name should look like a surname
        return LooksLikeSurname(value);
    }

    private bool LooksLikeSurname(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Should start with uppercase
        if (!char.IsUpper(value[0]) && !ScriptDetector.IsCyrillic(value[0]))
            return false;

        // Shouldn't have numbers
        if (value.Any(char.IsDigit))
            return false;

        // Shouldn't be too long (probably a note)
        if (value.Length > 30)
            return false;

        // Shouldn't have too many spaces (probably a phrase)
        if (value.Count(c => c == ' ') > 2)
            return false;

        return true;
    }
}
