using System.Globalization;

namespace GedcomGeniSync.Services.ML;

/// <summary>
/// Detects the script/alphabet used in a name and infers locale
/// </summary>
public static class ScriptDetector
{
    /// <summary>
    /// Detect the primary script used in a name
    /// </summary>
    public static NameScript DetectScript(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NameScript.Unknown;

        var scripts = new Dictionary<NameScript, int>();

        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c))
                continue;

            var script = GetCharacterScript(c);
            if (script != NameScript.Unknown)
            {
                scripts.TryGetValue(script, out var count);
                scripts[script] = count + 1;
            }
        }

        if (scripts.Count == 0)
            return NameScript.Unknown;

        if (scripts.Count > 1)
        {
            // If one script dominates (>80%), use it; otherwise mark as mixed
            var total = scripts.Values.Sum();
            var dominant = scripts.MaxBy(kv => kv.Value);
            if (dominant.Value > total * 0.8)
                return dominant.Key;
            return NameScript.Mixed;
        }

        return scripts.Keys.First();
    }

    /// <summary>
    /// Get the script of a single character
    /// </summary>
    private static NameScript GetCharacterScript(char c)
    {
        // Cyrillic: U+0400-U+04FF (main), U+0500-U+052F (supplement)
        if (c >= '\u0400' && c <= '\u052F')
            return NameScript.Cyrillic;

        // Latin: Basic Latin + Latin Extended
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '\u00C0' && c <= '\u024F') || // Latin Extended-A, Extended-B
            (c >= '\u1E00' && c <= '\u1EFF'))   // Latin Extended Additional
            return NameScript.Latin;

        // Hebrew: U+0590-U+05FF
        if (c >= '\u0590' && c <= '\u05FF')
            return NameScript.Hebrew;

        // Arabic: U+0600-U+06FF, U+0750-U+077F
        if ((c >= '\u0600' && c <= '\u06FF') || (c >= '\u0750' && c <= '\u077F'))
            return NameScript.Arabic;

        // Greek: U+0370-U+03FF
        if (c >= '\u0370' && c <= '\u03FF')
            return NameScript.Greek;

        return NameScript.Unknown;
    }

    /// <summary>
    /// Infer locale from detected script
    /// Note: This is a simplified mapping. Latin script can be many languages.
    /// </summary>
    public static string InferLocaleFromScript(NameScript script)
    {
        return script switch
        {
            NameScript.Cyrillic => "ru",  // Default to Russian for Cyrillic
            NameScript.Hebrew => "he",
            NameScript.Arabic => "ar",
            NameScript.Greek => "el",
            NameScript.Latin => "en",     // Default to English for Latin
            _ => "unknown"
        };
    }

    /// <summary>
    /// Infer locale from place name (birth place, death place)
    /// </summary>
    public static string? InferLocaleFromPlace(string? place)
    {
        if (string.IsNullOrWhiteSpace(place))
            return null;

        var lower = place.ToLowerInvariant();

        // Russian places
        if (ContainsAny(lower, "россия", "russia", "москва", "moscow", "санкт-петербург",
            "st. petersburg", "киев", "kyiv", "kiev", "одесса", "odessa", "минск", "minsk",
            "харьков", "kharkiv", "баку", "baku", "тбилиси", "tbilisi", "ереван", "yerevan",
            "ташкент", "tashkent", "алма-ата", "almaty", "новосибирск", "novosibirsk",
            "екатеринбург", "yekaterinburg", "казань", "kazan", "нижний новгород",
            "ростов", "ростов-на-дону", "воронеж", "самара", "омск", "челябинск",
            "уфа", "пермь", "волгоград", "красноярск", "саратов", "тюмень", "тула",
            "иркутск", "владивосток", "хабаровск", "краснодар", "оренбург", "рязань",
            "пенза", "липецк", "астрахань", "ярославль", "барнаул", "ульяновск",
            "томск", "кемерово", "курск", "владимир", "калуга", "смоленск", "брянск",
            "белгород", "тверь", "иваново", "калининград", "архангельск", "мурманск",
            "псков", "новгород", "вологда", "чебоксары", "сочи", "ставрополь",
            "imperio ruso", "russian empire", "российская империя", "ссср", "ussr"))
        {
            return "ru";
        }

        // Ukrainian places
        if (ContainsAny(lower, "україна", "ukraine", "украина", "львів", "lviv", "львов",
            "дніпро", "dnipro", "запоріжжя", "zaporizhzhia", "донецьк", "donetsk",
            "полтава", "вінниця", "чернігів", "житомир", "черкаси", "суми", "херсон",
            "миколаїв", "івано-франківськ", "тернопіль", "луцьк", "рівне", "хмельницький"))
        {
            return "uk";
        }

        // Hebrew/Israeli places
        if (ContainsAny(lower, "ישראל", "israel", "израиль", "jerusalem", "ירושלים",
            "tel aviv", "תל אביב", "haifa", "חיפה", "beer sheva", "באר שבע",
            "netanya", "נתניה", "ashdod", "אשדוד", "ramat gan", "רמת גן",
            "bnei brak", "בני ברק", "petah tikva", "פתח תקווה", "holon", "חולון"))
        {
            return "he";
        }

        // German places
        if (ContainsAny(lower, "germany", "deutschland", "германия", "berlin", "берлин",
            "munich", "münchen", "мюнхен", "hamburg", "гамбург", "frankfurt", "франкфурт",
            "cologne", "köln", "кёльн", "stuttgart", "штутгарт", "düsseldorf", "дюссельдорф",
            "dortmund", "дортмунд", "essen", "эссен", "leipzig", "лейпциг", "bremen", "бремен",
            "dresden", "дрезден", "hannover", "ганновер", "nürnberg", "нюрнберг"))
        {
            return "de";
        }

        // Polish places
        if (ContainsAny(lower, "poland", "polska", "польша", "warsaw", "warszawa", "варшава",
            "krakow", "kraków", "краков", "łódź", "лодзь", "wrocław", "вроцлав",
            "poznań", "познань", "gdańsk", "гданьск", "szczecin", "щецин", "lublin", "люблин"))
        {
            return "pl";
        }

        // Belarusian places
        if (ContainsAny(lower, "belarus", "беларусь", "белоруссия", "brest", "брест",
            "гомель", "gomel", "гродно", "grodno", "могилёв", "mogilev", "витебск", "vitebsk"))
        {
            return "be";
        }

        // Lithuanian places
        if (ContainsAny(lower, "lithuania", "lietuva", "литва", "vilnius", "вильнюс",
            "kaunas", "каунас", "klaipėda", "клайпеда", "šiauliai", "panevėžys"))
        {
            return "lt";
        }

        // Latvian places
        if (ContainsAny(lower, "latvia", "latvija", "латвия", "riga", "рига",
            "daugavpils", "даугавпилс", "liepāja", "лиепая"))
        {
            return "lv";
        }

        // USA/English
        if (ContainsAny(lower, "usa", "united states", "сша", "америка", "america",
            "new york", "нью-йорк", "los angeles", "лос-анджелес", "chicago", "чикаго",
            "houston", "хьюстон", "phoenix", "philadelphia", "san antonio", "san diego",
            "dallas", "austin", "jacksonville", "san francisco", "seattle", "denver",
            "boston", "бостон", "detroit", "детройт", "atlanta", "атланта",
            "miami", "майами", "washington", "вашингтон", "brooklyn", "бруклин",
            "queens", "manhattan", "манхэттен", "bronx", "бронкс", "staten island"))
        {
            return "en";
        }

        // UK/English
        if (ContainsAny(lower, "uk", "united kingdom", "великобритания", "england", "англия",
            "london", "лондон", "manchester", "манчестер", "birmingham", "бирмингем",
            "liverpool", "ливерпуль", "glasgow", "глазго", "edinburgh", "эдинбург",
            "leeds", "лидс", "sheffield", "bristol", "cardiff"))
        {
            return "en";
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (var value in values)
        {
            if (text.Contains(value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Infer locale from Cyrillic name patterns (Russian vs Ukrainian vs Belarusian)
    /// </summary>
    public static string InferCyrillicLocale(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "ru";

        // Ukrainian-specific letters: і, ї, є, ґ
        if (name.Any(c => c == 'і' || c == 'ї' || c == 'є' || c == 'ґ' ||
                         c == 'І' || c == 'Ї' || c == 'Є' || c == 'Ґ'))
            return "uk";

        // Belarusian-specific: ў (short u)
        if (name.Any(c => c == 'ў' || c == 'Ў'))
            return "be";

        // Default to Russian
        return "ru";
    }

    /// <summary>
    /// Refine Latin locale based on character patterns
    /// </summary>
    public static string InferLatinLocale(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "en";

        // German umlauts and ß
        if (name.Any(c => c == 'ä' || c == 'ö' || c == 'ü' || c == 'ß' ||
                         c == 'Ä' || c == 'Ö' || c == 'Ü'))
            return "de";

        // Polish-specific: ą, ć, ę, ł, ń, ó, ś, ź, ż
        if (name.Any(c => c == 'ą' || c == 'ć' || c == 'ę' || c == 'ł' ||
                         c == 'ń' || c == 'ś' || c == 'ź' || c == 'ż' ||
                         c == 'Ą' || c == 'Ć' || c == 'Ę' || c == 'Ł' ||
                         c == 'Ń' || c == 'Ś' || c == 'Ź' || c == 'Ż'))
            return "pl";

        // Lithuanian-specific: ą, č, ę, ė, į, š, ų, ū, ž
        if (name.Any(c => c == 'ė' || c == 'į' || c == 'ų' || c == 'ū' ||
                         c == 'Ė' || c == 'Į' || c == 'Ų' || c == 'Ū'))
            return "lt";

        // Latvian-specific: ā, č, ē, ģ, ī, ķ, ļ, ņ, š, ū, ž
        if (name.Any(c => c == 'ā' || c == 'ē' || c == 'ģ' || c == 'ī' ||
                         c == 'ķ' || c == 'ļ' || c == 'ņ' ||
                         c == 'Ā' || c == 'Ē' || c == 'Ģ' || c == 'Ī' ||
                         c == 'Ķ' || c == 'Ļ' || c == 'Ņ'))
            return "lv";

        // French-specific: ç, œ, and common accented vowels
        if (name.Any(c => c == 'ç' || c == 'œ' || c == 'Ç' || c == 'Œ'))
            return "fr";

        // Default to English for unrecognized Latin
        return "en";
    }
}
