namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for Ukrainian names.
/// Detects Ukrainian-specific letters (і, ї, є, ґ) and surname patterns (-енко, -чук).
/// Uses Cyrillic.Convert for BGN/PCGN transliteration.
/// </summary>
public class UkrainianHandler : LanguageDetectionHandlerBase
{
    public override string Name => "Ukrainian";
    public override int Order => 24;

    protected override string TargetLocale => Locales.Ukrainian;
    protected override bool SkipCyrillic => false;  // We look for Cyrillic!
    protected override bool CheckAllLocales => true;

    protected override bool DetectLanguage(string text)
        => ScriptDetector.IsUkrainian(text);
}
