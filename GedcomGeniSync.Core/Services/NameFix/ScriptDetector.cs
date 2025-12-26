using System.Text.RegularExpressions;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Utility class for detecting Unicode scripts and specific languages in text.
/// Used to determine which locale names should be placed in.
/// </summary>
public static class ScriptDetector
{
    #region Script Detection

    /// <summary>
    /// Unicode script types relevant for genealogical names
    /// </summary>
    public enum TextScript
    {
        Unknown,
        Latin,
        Cyrillic,
        Hebrew,
        Arabic,
        Greek,
        Mixed  // Contains multiple scripts
    }

    /// <summary>
    /// Detect the primary script used in text
    /// </summary>
    public static TextScript DetectScript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TextScript.Unknown;

        var latinCount = 0;
        var cyrillicCount = 0;
        var hebrewCount = 0;
        var arabicCount = 0;
        var greekCount = 0;

        foreach (var c in text)
        {
            if (IsLatin(c)) latinCount++;
            else if (IsCyrillic(c)) cyrillicCount++;
            else if (IsHebrew(c)) hebrewCount++;
            else if (IsArabic(c)) arabicCount++;
            else if (IsGreek(c)) greekCount++;
        }

        var total = latinCount + cyrillicCount + hebrewCount + arabicCount + greekCount;
        if (total == 0) return TextScript.Unknown;

        // Check for mixed scripts
        var scriptsUsed = new[] { latinCount, cyrillicCount, hebrewCount, arabicCount, greekCount }
            .Count(x => x > 0);

        if (scriptsUsed > 1) return TextScript.Mixed;

        // Return dominant script
        if (latinCount > 0) return TextScript.Latin;
        if (cyrillicCount > 0) return TextScript.Cyrillic;
        if (hebrewCount > 0) return TextScript.Hebrew;
        if (arabicCount > 0) return TextScript.Arabic;
        if (greekCount > 0) return TextScript.Greek;

        return TextScript.Unknown;
    }

    /// <summary>
    /// Check if text contains Cyrillic characters
    /// </summary>
    public static bool ContainsCyrillic(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Any(IsCyrillic);
    }

    /// <summary>
    /// Check if text contains Latin characters
    /// </summary>
    public static bool ContainsLatin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Any(IsLatin);
    }

    /// <summary>
    /// Check if text is purely Cyrillic (ignoring whitespace and punctuation)
    /// </summary>
    public static bool IsPurelyCyrillic(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.All(c => IsCyrillic(c) || !char.IsLetter(c));
    }

    /// <summary>
    /// Check if text is purely Latin (ignoring whitespace and punctuation)
    /// </summary>
    public static bool IsPurelyLatin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.All(c => IsLatin(c) || !char.IsLetter(c));
    }

    #endregion

    #region Character Classification

    /// <summary>
    /// Check if character is in Cyrillic range (includes Russian, Ukrainian, etc.)
    /// </summary>
    public static bool IsCyrillic(char c)
    {
        // Basic Cyrillic: U+0400-U+04FF
        // Cyrillic Supplement: U+0500-U+052F
        // Cyrillic Extended-A: U+2DE0-U+2DFF
        // Cyrillic Extended-B: U+A640-U+A69F
        return (c >= 0x0400 && c <= 0x052F) ||
               (c >= 0x2DE0 && c <= 0x2DFF) ||
               (c >= 0xA640 && c <= 0xA69F);
    }

    /// <summary>
    /// Check if character is in Latin range (includes extended Latin for European languages)
    /// </summary>
    public static bool IsLatin(char c)
    {
        // Basic Latin letters: A-Z, a-z
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;

        // Latin Extended-A: U+0100-U+017F (Lithuanian, Estonian, Polish, etc.)
        // Latin Extended-B: U+0180-U+024F
        // Latin Extended Additional: U+1E00-U+1EFF
        return (c >= 0x00C0 && c <= 0x00FF) ||  // Latin-1 Supplement (ä, ö, ü, etc.)
               (c >= 0x0100 && c <= 0x024F) ||  // Latin Extended-A and B
               (c >= 0x1E00 && c <= 0x1EFF);    // Latin Extended Additional
    }

    /// <summary>
    /// Check if character is Hebrew
    /// </summary>
    public static bool IsHebrew(char c)
    {
        // Hebrew: U+0590-U+05FF
        return c >= 0x0590 && c <= 0x05FF;
    }

    /// <summary>
    /// Check if character is Arabic
    /// </summary>
    public static bool IsArabic(char c)
    {
        // Arabic: U+0600-U+06FF
        // Arabic Supplement: U+0750-U+077F
        return (c >= 0x0600 && c <= 0x06FF) ||
               (c >= 0x0750 && c <= 0x077F);
    }

    /// <summary>
    /// Check if character is Greek
    /// </summary>
    public static bool IsGreek(char c)
    {
        // Greek: U+0370-U+03FF
        // Greek Extended: U+1F00-U+1FFF
        return (c >= 0x0370 && c <= 0x03FF) ||
               (c >= 0x1F00 && c <= 0x1FFF);
    }

    #endregion

    #region Language Detection

    /// <summary>
    /// Detected language with confidence
    /// </summary>
    public record LanguageDetectionResult(string LanguageCode, double Confidence, string Reason);

    /// <summary>
    /// Check if character is basic Latin (A-Z, a-z only, no diacritics)
    /// </summary>
    public static bool IsLatinLetter(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }

    /// <summary>
    /// Check if text contains Ukrainian-specific characters or patterns
    /// </summary>
    public static bool IsUkrainian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Must contain Cyrillic first
        if (!ContainsCyrillic(text)) return false;

        // Ukrainian-specific letters (definitive)
        var ukChars = new[] { 'і', 'І', 'ї', 'Ї', 'є', 'Є', 'ґ', 'Ґ' };
        if (text.Any(c => ukChars.Contains(c))) return true;

        // Common Ukrainian surname patterns
        var lowerText = text.ToLowerInvariant();
        var ukSurnameEndings = new[] { "енко", "ейко", "ченко", "шенко", "чук", "щук" };
        return ukSurnameEndings.Any(e => lowerText.EndsWith(e));
    }

    /// <summary>
    /// Detect specific language for Latin text
    /// </summary>
    public static LanguageDetectionResult? DetectLatinLanguage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Check for Lithuanian
        if (IsLithuanian(text))
        {
            var confidence = CalculateLithuanianConfidence(text);
            return new LanguageDetectionResult(Locales.Lithuanian, confidence, "Lithuanian characters or patterns detected");
        }

        // Check for Estonian
        if (IsEstonian(text))
        {
            var confidence = CalculateEstonianConfidence(text);
            return new LanguageDetectionResult(Locales.Estonian, confidence, "Estonian characters detected");
        }

        // Check for Latvian
        if (IsLatvian(text))
        {
            var confidence = CalculateLatvianConfidence(text);
            return new LanguageDetectionResult(Locales.Latvian, confidence, "Latvian characters or patterns detected");
        }

        // Check for Polish
        if (IsPolish(text))
        {
            var confidence = CalculatePolishConfidence(text);
            return new LanguageDetectionResult(Locales.Polish, confidence, "Polish characters detected");
        }

        // Check for German
        if (IsGerman(text))
        {
            var confidence = CalculateGermanConfidence(text);
            return new LanguageDetectionResult(Locales.German, confidence, "German characters detected");
        }

        // Default to English for plain Latin
        return new LanguageDetectionResult(Locales.English, 0.5, "Default Latin script");
    }

    /// <summary>
    /// Check if text contains Lithuanian-specific characters or patterns
    /// </summary>
    public static bool IsLithuanian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Lithuanian-specific letters
        var ltChars = new[] { 'ą', 'č', 'ę', 'ė', 'į', 'š', 'ų', 'ū', 'ž', 'Ą', 'Č', 'Ę', 'Ė', 'Į', 'Š', 'Ų', 'Ū', 'Ž' };
        if (text.Any(c => ltChars.Contains(c))) return true;

        // Lithuanian surname endings (masculine)
        var ltMasculineEndings = new[] { "auskas", "aitis", "ūnas", "ėnas", "onis", "inis", "ulis" };
        // Lithuanian surname endings (feminine)
        var ltFeminineEndings = new[] { "ienė", "ytė", "aitė", "utė", "ūtė" };

        var lowerText = text.ToLowerInvariant();
        return ltMasculineEndings.Any(e => lowerText.EndsWith(e)) ||
               ltFeminineEndings.Any(e => lowerText.EndsWith(e));
    }

    /// <summary>
    /// Check if text contains Estonian-specific characters
    /// </summary>
    public static bool IsEstonian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Estonian-specific letters (õ is unique to Estonian)
        var etChars = new[] { 'õ', 'Õ' };
        if (text.Any(c => etChars.Contains(c))) return true;

        // Common Estonian surname patterns
        var etPatterns = new[] { "mäe", "mets", "saar", "pere", "nurm", "vald", "järv" };
        var lowerText = text.ToLowerInvariant();
        return etPatterns.Any(p => lowerText.Contains(p));
    }

    /// <summary>
    /// Check if text contains Latvian-specific characters or patterns
    /// </summary>
    public static bool IsLatvian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Latvian-specific letters
        var lvChars = new[] { 'ā', 'č', 'ē', 'ģ', 'ī', 'ķ', 'ļ', 'ņ', 'š', 'ū', 'ž',
                              'Ā', 'Č', 'Ē', 'Ģ', 'Ī', 'Ķ', 'Ļ', 'Ņ', 'Š', 'Ū', 'Ž' };
        if (text.Any(c => lvChars.Contains(c))) return true;

        // Latvian surname endings
        var lvEndings = new[] { "iņš", "āns", "ēns" };
        var lowerText = text.ToLowerInvariant();
        return lvEndings.Any(e => lowerText.EndsWith(e));
    }

    /// <summary>
    /// Check if text contains Polish-specific characters
    /// </summary>
    public static bool IsPolish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Polish-specific letters
        var plChars = new[] { 'ą', 'ć', 'ę', 'ł', 'ń', 'ó', 'ś', 'ź', 'ż',
                              'Ą', 'Ć', 'Ę', 'Ł', 'Ń', 'Ó', 'Ś', 'Ź', 'Ż' };
        return text.Any(c => plChars.Contains(c));
    }

    /// <summary>
    /// Check if text contains German-specific characters
    /// </summary>
    public static bool IsGerman(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // German-specific: ß (Eszett) is unique to German
        if (text.Contains('ß') || text.Contains('ẞ')) return true;

        // German umlauts are shared with other languages,
        // but combined with certain patterns suggest German
        var deChars = new[] { 'ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü' };
        if (!text.Any(c => deChars.Contains(c))) return false;

        // German surname patterns
        var dePatterns = new[] { "müller", "schmidt", "schneider", "fischer", "meyer", "weber",
                                  "schäfer", "köhler", "böhm", "günther", "größ", "weiß" };
        var lowerText = text.ToLowerInvariant();
        return dePatterns.Any(p => lowerText.Contains(p));
    }

    #endregion

    #region Confidence Calculation

    private static double CalculateLithuanianConfidence(string text)
    {
        var ltChars = new[] { 'ą', 'č', 'ę', 'ė', 'į', 'š', 'ų', 'ū', 'ž', 'Ą', 'Č', 'Ę', 'Ė', 'Į', 'Š', 'Ų', 'Ū', 'Ž' };
        var ltCharCount = text.Count(c => ltChars.Contains(c));

        // High confidence if multiple Lithuanian-specific characters
        if (ltCharCount >= 2) return 0.95;
        if (ltCharCount == 1) return 0.85;

        // Pattern-based confidence
        var lowerText = text.ToLowerInvariant();
        var ltEndings = new[] { "auskas", "aitis", "ūnas", "ėnas", "ienė", "ytė", "aitė" };
        if (ltEndings.Any(e => lowerText.EndsWith(e))) return 0.90;

        return 0.70;
    }

    private static double CalculateEstonianConfidence(string text)
    {
        // õ is unique to Estonian
        if (text.Contains('õ') || text.Contains('Õ')) return 0.95;

        var etChars = new[] { 'ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü' };
        var etPatterns = new[] { "mäe", "mets", "saar", "pere" };
        var lowerText = text.ToLowerInvariant();

        if (text.Any(c => etChars.Contains(c)) && etPatterns.Any(p => lowerText.Contains(p)))
            return 0.85;

        return 0.70;
    }

    private static double CalculateLatvianConfidence(string text)
    {
        var lvChars = new[] { 'ā', 'ē', 'ī', 'ū', 'ģ', 'ķ', 'ļ', 'ņ', 'Ā', 'Ē', 'Ī', 'Ū', 'Ģ', 'Ķ', 'Ļ', 'Ņ' };
        var lvCharCount = text.Count(c => lvChars.Contains(c));

        if (lvCharCount >= 2) return 0.95;
        if (lvCharCount == 1) return 0.85;

        return 0.70;
    }

    private static double CalculatePolishConfidence(string text)
    {
        // ł is fairly unique to Polish among Slavic languages written in Latin
        if (text.Contains('ł') || text.Contains('Ł')) return 0.90;

        var plChars = new[] { 'ą', 'ć', 'ę', 'ń', 'ó', 'ś', 'ź', 'ż', 'Ą', 'Ć', 'Ę', 'Ń', 'Ó', 'Ś', 'Ź', 'Ż' };
        var plCharCount = text.Count(c => plChars.Contains(c));

        if (plCharCount >= 2) return 0.85;
        if (plCharCount == 1) return 0.75;

        return 0.60;
    }

    private static double CalculateGermanConfidence(string text)
    {
        if (text.Contains('ß') || text.Contains('ẞ')) return 0.95;

        return 0.60; // Umlauts are shared with other languages
    }

    #endregion

    #region Text Splitting

    /// <summary>
    /// Split mixed-script text into script-specific parts
    /// </summary>
    public static Dictionary<TextScript, string> SplitByScript(string? text)
    {
        var result = new Dictionary<TextScript, string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        // Split by common separators
        var separators = new[] { ' ', '/', '|', '(', ')', '[', ']', '-' };
        var tokens = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var latinParts = new List<string>();
        var cyrillicParts = new List<string>();
        var hebrewParts = new List<string>();

        foreach (var token in tokens)
        {
            var script = DetectScript(token);
            switch (script)
            {
                case TextScript.Latin:
                    latinParts.Add(token);
                    break;
                case TextScript.Cyrillic:
                    cyrillicParts.Add(token);
                    break;
                case TextScript.Hebrew:
                    hebrewParts.Add(token);
                    break;
                case TextScript.Mixed:
                    // For mixed tokens, try to extract each script
                    var (latin, cyrillic) = SplitMixedToken(token);
                    if (!string.IsNullOrEmpty(latin)) latinParts.Add(latin);
                    if (!string.IsNullOrEmpty(cyrillic)) cyrillicParts.Add(cyrillic);
                    break;
            }
        }

        if (latinParts.Count > 0)
            result[TextScript.Latin] = string.Join(" ", latinParts);

        if (cyrillicParts.Count > 0)
            result[TextScript.Cyrillic] = string.Join(" ", cyrillicParts);

        if (hebrewParts.Count > 0)
            result[TextScript.Hebrew] = string.Join(" ", hebrewParts);

        return result;
    }

    /// <summary>
    /// Split a single token that contains both Latin and Cyrillic characters
    /// </summary>
    private static (string Latin, string Cyrillic) SplitMixedToken(string token)
    {
        var latin = new List<char>();
        var cyrillic = new List<char>();

        foreach (var c in token)
        {
            if (IsLatin(c)) latin.Add(c);
            else if (IsCyrillic(c)) cyrillic.Add(c);
            // Ignore non-letter characters
        }

        return (new string(latin.ToArray()), new string(cyrillic.ToArray()));
    }

    #endregion
}
