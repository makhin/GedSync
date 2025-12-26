namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that detects Estonian names and copies them to the Estonian locale.
/// Estonian names often contain õ, ä, ö, ü and patterns like -mäe, -saar, -mets.
/// </summary>
public class EstonianHandler : LanguageDetectionHandlerBase
{
    public override string Name => "Estonian";
    public override int Order => 26;

    protected override string TargetLocale => Locales.Estonian;
    protected override bool SkipCyrillic => true;

    protected override bool DetectLanguage(string text)
        => ScriptDetector.IsEstonian(text);
}
