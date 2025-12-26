namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects Lithuanian names and copies them to the Lithuanian locale.
/// Lithuanian names have distinctive patterns: ą, č, ę, ė, etc. and endings like -auskas, -aitis.
/// </summary>
public class LithuanianHandler : LanguageDetectionHandlerBase
{
    public override string Name => "Lithuanian";
    public override int Order => 25;

    protected override string TargetLocale => Locales.Lithuanian;
    protected override bool SkipCyrillic => true;

    protected override bool DetectLanguage(string text)
        => ScriptDetector.IsLithuanian(text);
}
