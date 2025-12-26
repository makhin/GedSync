using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that removes special characters and garbage from names.
/// Runs first to clean up data before other handlers process it.
/// </summary>
public class SpecialCharsCleanupHandler : NameFixHandlerBase
{
    public override string Name => "SpecialCharsCleanup";
    public override int Order => 5;

    // Characters to remove from start/end of names
    private static readonly char[] TrimChars = { '*', '?', '#', '~', '`', '^', '+', '=', '<', '>', '|', '\\', '/', '_' };

    // Regex for leading numbers (but keep Roman numerals at end for suffixes)
    private static readonly Regex LeadingNumbers = new(@"^\d+\s*", RegexOptions.Compiled);

    // Regex for multiple spaces
    private static readonly Regex MultipleSpaces = new(@"\s{2,}", RegexOptions.Compiled);

    // Characters that should not appear in names at all
    private static readonly Regex InvalidChars = new(@"[\[\]{}@#$%^&*+=<>|\\~`]", RegexOptions.Compiled);

    public override void Handle(NameFixContext context)
    {
        // Process all fields using helper methods
        ForEachLocaleField(context, CleanValue, "Removed special characters");
        ForEachPrimaryField(context, CleanValue, "Removed special characters");
    }

    private static string CleanValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var result = value;

        // Remove leading/trailing special characters
        result = result.Trim(TrimChars);

        // Remove leading numbers
        result = LeadingNumbers.Replace(result, "");

        // Remove invalid characters
        result = InvalidChars.Replace(result, "");

        // Normalize multiple spaces to single
        result = MultipleSpaces.Replace(result, " ");

        return result.Trim();
    }
}
