using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that extracts titles (Dr., Prof., князь, граф, etc.) from names
/// and moves them to the title field.
/// </summary>
public class TitleExtractHandler : NameFixHandlerBase
{
    public override string Name => "TitleExtract";
    public override int Order => 8;

    // Common titles in various languages
    private static readonly string[] LatinTitles = new[]
    {
        // Academic
        "Dr.", "Dr", "Prof.", "Prof", "PhD", "Ph.D.", "M.D.", "MD",
        // Religious
        "Rev.", "Rev", "Fr.", "Fr", "Sr.", "Pastor", "Rabbi", "Imam",
        // Military
        "Gen.", "Gen", "Col.", "Col", "Maj.", "Maj", "Capt.", "Capt", "Lt.", "Lt", "Sgt.", "Sgt",
        // Noble (English)
        "Sir", "Dame", "Lord", "Lady", "Duke", "Duchess", "Earl", "Count", "Countess", "Baron", "Baroness",
        "Prince", "Princess", "King", "Queen",
        // Professional
        "Atty.", "Atty", "Hon.", "Hon", "Judge"
    };

    private static readonly string[] CyrillicTitles = new[]
    {
        // Noble (Russian)
        "князь", "княгиня", "княжна",
        "граф", "графиня",
        "барон", "баронесса",
        "герцог", "герцогиня",
        "царь", "царица", "царевич", "царевна",
        "император", "императрица",
        // Religious
        "отец", "батюшка", "матушка",
        "протоиерей", "иерей", "диакон",
        "митрополит", "архиепископ", "епископ", "архимандрит",
        // Military
        "генерал", "полковник", "майор", "капитан", "лейтенант",
        // Academic
        "профессор", "доктор", "академик"
    };

    // Regex to match title at the beginning of a name
    private readonly Regex _latinTitleRegex;
    private readonly Regex _cyrillicTitleRegex;

    public TitleExtractHandler()
    {
        // Build regex patterns
        var latinPattern = @"^(" + string.Join("|", LatinTitles.Select(Regex.Escape)) + @")\s+";
        _latinTitleRegex = new Regex(latinPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var cyrillicPattern = @"^(" + string.Join("|", CyrillicTitles.Select(Regex.Escape)) + @")\s+";
        _cyrillicTitleRegex = new Regex(cyrillicPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public override void Handle(NameFixContext context)
    {
        // Check primary FirstName field
        ExtractFromPrimaryField(context);

        // Check localized first names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ExtractFromLocale(context, locale);
        }
    }

    private void ExtractFromPrimaryField(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.FirstName)) return;

        var (title, remainingName) = ExtractTitle(context.FirstName);
        if (title == null) return;

        // Only set if we don't already have a title
        if (string.IsNullOrWhiteSpace(context.Suffix)) // Using Suffix as title placeholder
        {
            context.Changes.Add(new NameChange
            {
                Field = "FirstName",
                OldValue = context.FirstName,
                NewValue = remainingName,
                Reason = $"Extracted title '{title}' from first name",
                Handler = Name
            });

            context.FirstName = remainingName;
            // Note: Title field would go here if PersonRecord had it
        }
    }

    private void ExtractFromLocale(NameFixContext context, string locale)
    {
        var firstName = context.GetName(locale, NameFields.FirstName);
        if (string.IsNullOrWhiteSpace(firstName)) return;

        var (title, remainingName) = ExtractTitle(firstName);
        if (title == null) return;

        SetName(context, locale, NameFields.FirstName, remainingName,
            $"Extracted title '{title}' from first name");

        // Store title in title field if available
        var existingTitle = context.GetName(locale, NameFields.Title);
        if (string.IsNullOrWhiteSpace(existingTitle))
        {
            SetName(context, locale, NameFields.Title, title,
                $"Title extracted from first name");
        }
    }

    private (string? Title, string RemainingName) ExtractTitle(string name)
    {
        // Try Latin titles first
        var latinMatch = _latinTitleRegex.Match(name);
        if (latinMatch.Success)
        {
            var title = latinMatch.Groups[1].Value;
            var remaining = name.Substring(latinMatch.Length).Trim();
            return (title, remaining);
        }

        // Try Cyrillic titles
        var cyrillicMatch = _cyrillicTitleRegex.Match(name);
        if (cyrillicMatch.Success)
        {
            var title = cyrillicMatch.Groups[1].Value;
            var remaining = name.Substring(cyrillicMatch.Length).Trim();
            return (title, remaining);
        }

        return (null, name);
    }
}
