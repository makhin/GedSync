namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for Hebrew names.
/// Detects Hebrew script and copies to he locale.
/// Uses Unidecode.NET for transliteration to Latin.
/// </summary>
public class HebrewHandler : LanguageDetectionHandlerBase
{
    public override string Name => "Hebrew";
    public override int Order => 28;

    protected override string TargetLocale => Locales.Hebrew;
    protected override bool SkipCyrillic => false;
    protected override bool CheckAllLocales => true;

    protected override bool DetectLanguage(string text)
        => text.Any(c => ScriptDetector.IsHebrew(c));

    /// <summary>
    /// Extract only Hebrew characters from mixed text
    /// </summary>
    protected override string ExtractRelevantPortion(string text)
    {
        var hebrewChars = text.Where(c =>
            ScriptDetector.IsHebrew(c) ||
            char.IsWhiteSpace(c) ||
            c == '-' ||
            c == '\'');
        return new string(hebrewChars.ToArray()).Trim();
    }
}
