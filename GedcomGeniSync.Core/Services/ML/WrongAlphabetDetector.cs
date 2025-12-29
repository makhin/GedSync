using Microsoft.ML;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.ML;

/// <summary>
/// Detects names written in the "wrong" alphabet:
/// - Russian names written in Latin (transliteration)
/// - English names written in Cyrillic (phonetic transcription)
///
/// Uses a combination of:
/// 1. Known names dictionary
/// 2. Transliteration pattern detection
/// 3. ML.NET character n-gram classification
/// </summary>
public class WrongAlphabetDetector
{
    private readonly ILogger _logger;
    private readonly MLContext _mlContext;
    private ITransformer? _model;
    private PredictionEngine<NameScriptInput, NameScriptPrediction>? _predictionEngine;

    // Known Russian names (stored lowercase for matching)
    private static readonly HashSet<string> KnownRussianNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Male first names
        "ivan", "dmitry", "dmitri", "sergey", "sergei", "alexander", "alexey", "alexei", "andrey", "andrei",
        "mikhail", "nikolay", "nikolai", "vladimir", "viktor", "yuri", "yury", "pavel", "oleg", "igor",
        "boris", "vasily", "vasili", "grigory", "grigori", "anatoly", "anatoli", "konstantin", "leonid",
        "maxim", "maksim", "evgeny", "evgeni", "yevgeny", "yevgeni", "denis", "roman", "artem", "kirill",
        "stanislav", "vyacheslav", "valery", "valeri", "gennady", "gennadi", "ruslan", "timofey", "timofei",
        "fyodor", "fedor", "semyon", "semion", "ilya", "nikita", "anton", "daniil", "egor", "yegor",
        "vitaly", "vitali", "vladislav", "gleb", "lev", "matvey", "matvei", "yaroslav", "stepan",

        // Female first names
        "anna", "maria", "mariya", "elena", "olga", "natalia", "natalya", "irina", "tatiana", "tatyana",
        "ekaterina", "svetlana", "lyudmila", "ludmila", "galina", "nina", "valentina", "vera", "larisa",
        "nadezhda", "tamara", "marina", "oksana", "yulia", "yuliya", "julia", "anastasia", "anastasiya",
        "alexandra", "alexandra", "elizaveta", "daria", "darya", "polina", "kristina", "alina", "diana",
        "kseniya", "ksenia", "xenia", "sofia", "sofiya", "veronika", "viktoria", "viktoriya", "evgeniya",
        "yevgeniya", "zinaida", "lyubov", "ludmila", "raisa", "alla", "albina", "klavdia", "klaudia",

        // Surnames (common patterns)
        "ivanov", "ivanova", "petrov", "petrova", "sidorov", "sidorova", "smirnov", "smirnova",
        "kuznetsov", "kuznetsova", "popov", "popova", "sokolov", "sokolova", "lebedev", "lebedeva",
        "kozlov", "kozlova", "novikov", "novikova", "morozov", "morozova", "volkov", "volkova",
        "alekseev", "alekseeva", "fedorov", "fedorova", "mikhailov", "mikhailova", "belov", "belova",
        "makarov", "makarova", "kovalev", "kovaleva", "ilyin", "ilyina", "gusev", "guseva",
        "titov", "titova", "orlov", "orlova", "andreev", "andreeva", "nikolaev", "nikolaeva"
    };

    // Known English names (stored lowercase for matching)
    private static readonly HashSet<string> KnownEnglishNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Male first names
        "michael", "john", "david", "james", "robert", "william", "richard", "joseph", "thomas", "charles",
        "christopher", "daniel", "matthew", "anthony", "mark", "donald", "steven", "paul", "andrew", "joshua",
        "kenneth", "kevin", "brian", "george", "timothy", "ronald", "edward", "jason", "jeffrey", "ryan",
        "jacob", "gary", "nicholas", "eric", "jonathan", "stephen", "larry", "justin", "scott", "brandon",
        "benjamin", "samuel", "raymond", "gregory", "frank", "alexander", "patrick", "jack", "dennis", "jerry",
        "tyler", "aaron", "jose", "adam", "nathan", "henry", "douglas", "zachary", "peter", "kyle",

        // Female first names
        "mary", "patricia", "jennifer", "linda", "barbara", "elizabeth", "susan", "jessica", "sarah", "karen",
        "lisa", "nancy", "betty", "margaret", "sandra", "ashley", "kimberly", "emily", "donna", "michelle",
        "dorothy", "carol", "amanda", "melissa", "deborah", "stephanie", "rebecca", "sharon", "laura", "cynthia",
        "kathleen", "amy", "angela", "shirley", "anna", "brenda", "pamela", "emma", "nicole", "helen",
        "samantha", "katherine", "christine", "debra", "rachel", "carolyn", "janet", "catherine", "maria",
        "heather", "diane", "ruth", "julie", "olivia", "joyce", "virginia", "victoria", "kelly", "lauren",

        // Common surnames
        "smith", "johnson", "williams", "brown", "jones", "garcia", "miller", "davis", "rodriguez", "martinez",
        "hernandez", "lopez", "gonzalez", "wilson", "anderson", "thomas", "taylor", "moore", "jackson", "martin",
        "lee", "thompson", "white", "harris", "sanchez", "clark", "ramirez", "lewis", "robinson", "walker",
        "young", "allen", "king", "wright", "scott", "torres", "nguyen", "hill", "flores", "green",
        "adams", "nelson", "baker", "hall", "rivera", "campbell", "mitchell", "carter", "roberts", "gomez"
    };

    // Known English names written in Cyrillic (phonetic transcription)
    private static readonly Dictionary<string, string> EnglishNamesCyrillic = new(StringComparer.OrdinalIgnoreCase)
    {
        // Male
        ["майкл"] = "Michael", ["джон"] = "John", ["дэвид"] = "David", ["девид"] = "David",
        ["джеймс"] = "James", ["роберт"] = "Robert", ["уильям"] = "William", ["вильям"] = "William",
        ["ричард"] = "Richard", ["джозеф"] = "Joseph", ["томас"] = "Thomas", ["чарльз"] = "Charles",
        ["кристофер"] = "Christopher", ["дэниел"] = "Daniel", ["даниэл"] = "Daniel",
        ["мэттью"] = "Matthew", ["мэтью"] = "Matthew", ["энтони"] = "Anthony", ["антони"] = "Anthony",
        ["марк"] = "Mark", ["дональд"] = "Donald", ["стивен"] = "Steven", ["стивэн"] = "Steven",
        ["пол"] = "Paul", ["эндрю"] = "Andrew", ["джошуа"] = "Joshua",
        ["кеннет"] = "Kenneth", ["кевин"] = "Kevin", ["брайан"] = "Brian",
        ["джордж"] = "George", ["тимоти"] = "Timothy", ["рональд"] = "Ronald",
        ["эдвард"] = "Edward", ["джейсон"] = "Jason", ["джеффри"] = "Jeffrey",
        ["райан"] = "Ryan", ["джейкоб"] = "Jacob", ["гари"] = "Gary",
        ["николас"] = "Nicholas", ["эрик"] = "Eric", ["джонатан"] = "Jonathan",
        ["стефен"] = "Stephen", ["ларри"] = "Larry", ["джастин"] = "Justin",
        ["скотт"] = "Scott", ["брэндон"] = "Brandon", ["бенджамин"] = "Benjamin",
        ["сэмюэл"] = "Samuel", ["рэймонд"] = "Raymond", ["грегори"] = "Gregory",
        ["фрэнк"] = "Frank", ["патрик"] = "Patrick", ["джек"] = "Jack",
        ["деннис"] = "Dennis", ["джерри"] = "Jerry", ["тайлер"] = "Tyler",
        ["аарон"] = "Aaron", ["адам"] = "Adam", ["нэйтан"] = "Nathan",
        ["генри"] = "Henry", ["дуглас"] = "Douglas", ["закари"] = "Zachary",
        ["питер"] = "Peter", ["кайл"] = "Kyle",

        // Female
        ["мэри"] = "Mary", ["патриция"] = "Patricia", ["дженнифер"] = "Jennifer",
        ["линда"] = "Linda", ["барбара"] = "Barbara", ["элизабет"] = "Elizabeth",
        ["сьюзан"] = "Susan", ["джессика"] = "Jessica", ["сара"] = "Sarah", ["карен"] = "Karen",
        ["лиза"] = "Lisa", ["нэнси"] = "Nancy", ["бетти"] = "Betty",
        ["маргарет"] = "Margaret", ["сандра"] = "Sandra", ["эшли"] = "Ashley",
        ["кимберли"] = "Kimberly", ["эмили"] = "Emily", ["донна"] = "Donna",
        ["мишель"] = "Michelle", ["дороти"] = "Dorothy", ["кэрол"] = "Carol",
        ["аманда"] = "Amanda", ["мелисса"] = "Melissa", ["дебора"] = "Deborah",
        ["стефани"] = "Stephanie", ["ребекка"] = "Rebecca", ["шэрон"] = "Sharon",
        ["лора"] = "Laura", ["синтия"] = "Cynthia", ["кэтлин"] = "Kathleen",
        ["эми"] = "Amy", ["анжела"] = "Angela", ["ширли"] = "Shirley",
        ["бренда"] = "Brenda", ["памела"] = "Pamela", ["эмма"] = "Emma",
        ["николь"] = "Nicole", ["хелен"] = "Helen", ["саманта"] = "Samantha",
        ["кэтрин"] = "Katherine", ["кристин"] = "Christine", ["дебра"] = "Debra",
        ["рейчел"] = "Rachel", ["кэролин"] = "Carolyn", ["джанет"] = "Janet",
        ["кэтрин"] = "Catherine", ["хизер"] = "Heather", ["дайан"] = "Diane",
        ["руфь"] = "Ruth", ["джули"] = "Julie", ["оливия"] = "Olivia",
        ["джойс"] = "Joyce", ["виржиния"] = "Virginia", ["келли"] = "Kelly",
        ["лорен"] = "Lauren"
    };

    // Patterns typical for Russian names transliterated to Latin
    private static readonly string[] RussianTranslitPatterns =
    {
        // Consonant clusters from transliteration
        "shch", "tsch", "sch",  // Щ
        "zh",   // Ж
        "kh",   // Х
        "ch",   // Ч
        "sh",   // Ш (but careful - also English)
        "ts",   // Ц

        // Typical endings
        "ov", "ev", "in", "ovich", "evich", "ovna", "evna",
        "sky", "skiy", "skaya", "ski",
        "enko", "enko", "uk", "yuk", "chuk",
        "kin", "nikov", "nikov",

        // Vowel patterns from transliteration
        "iya", "aya", "oya",  // Feminine endings
        "yev", "yov",         // Patronymics
        "ii", "iy", "yi",     // Double i patterns
        "yu", "ya", "ye",     // Й + vowel
    };

    // Patterns typical for English names phonetically transcribed to Cyrillic
    private static readonly string[] EnglishCyrillicPatterns =
    {
        "дж",   // J sound
        "тч",   // Ch sound
        "шн",   // -tion ending
        "эй",   // A as in "day"
        "оу",   // O as in "go"
        "ай",   // I as in "my"
        "ью",   // U as in "new"
    };

    // Known Russian names: Latin -> Cyrillic mappings
    private static readonly Dictionary<string, string> RussianNamesLatinToCyrillic = new(StringComparer.OrdinalIgnoreCase)
    {
        // Male first names
        ["ivan"] = "Иван", ["dmitry"] = "Дмитрий", ["dmitri"] = "Дмитрий",
        ["sergey"] = "Сергей", ["sergei"] = "Сергей", ["alexander"] = "Александр",
        ["alexey"] = "Алексей", ["alexei"] = "Алексей", ["andrey"] = "Андрей", ["andrei"] = "Андрей",
        ["mikhail"] = "Михаил", ["nikolay"] = "Николай", ["nikolai"] = "Николай",
        ["vladimir"] = "Владимир", ["viktor"] = "Виктор", ["yuri"] = "Юрий", ["yury"] = "Юрий",
        ["pavel"] = "Павел", ["oleg"] = "Олег", ["igor"] = "Игорь", ["boris"] = "Борис",
        ["vasily"] = "Василий", ["vasili"] = "Василий", ["grigory"] = "Григорий", ["grigori"] = "Григорий",
        ["anatoly"] = "Анатолий", ["anatoli"] = "Анатолий", ["konstantin"] = "Константин",
        ["leonid"] = "Леонид", ["maxim"] = "Максим", ["maksim"] = "Максим",
        ["evgeny"] = "Евгений", ["evgeni"] = "Евгений", ["yevgeny"] = "Евгений",
        ["denis"] = "Денис", ["roman"] = "Роман", ["artem"] = "Артём", ["kirill"] = "Кирилл",
        ["stanislav"] = "Станислав", ["vyacheslav"] = "Вячеслав", ["valery"] = "Валерий",
        ["gennady"] = "Геннадий", ["ruslan"] = "Руслан", ["timofey"] = "Тимофей",
        ["fyodor"] = "Фёдор", ["fedor"] = "Фёдор", ["semyon"] = "Семён",
        ["ilya"] = "Илья", ["nikita"] = "Никита", ["anton"] = "Антон", ["daniil"] = "Даниил",
        ["egor"] = "Егор", ["yegor"] = "Егор", ["vitaly"] = "Виталий",
        ["vladislav"] = "Владислав", ["gleb"] = "Глеб", ["lev"] = "Лев",
        ["matvey"] = "Матвей", ["yaroslav"] = "Ярослав", ["stepan"] = "Степан",

        // Female first names
        ["anna"] = "Анна", ["maria"] = "Мария", ["mariya"] = "Мария",
        ["elena"] = "Елена", ["olga"] = "Ольга", ["natalia"] = "Наталья", ["natalya"] = "Наталья",
        ["irina"] = "Ирина", ["tatiana"] = "Татьяна", ["tatyana"] = "Татьяна",
        ["ekaterina"] = "Екатерина", ["svetlana"] = "Светлана", ["lyudmila"] = "Людмила",
        ["galina"] = "Галина", ["nina"] = "Нина", ["valentina"] = "Валентина",
        ["vera"] = "Вера", ["larisa"] = "Лариса", ["nadezhda"] = "Надежда",
        ["tamara"] = "Тамара", ["marina"] = "Марина", ["oksana"] = "Оксана",
        ["yulia"] = "Юлия", ["yuliya"] = "Юлия", ["julia"] = "Юлия",
        ["anastasia"] = "Анастасия", ["anastasiya"] = "Анастасия",
        ["alexandra"] = "Александра", ["elizaveta"] = "Елизавета",
        ["daria"] = "Дарья", ["darya"] = "Дарья", ["polina"] = "Полина",
        ["kristina"] = "Кристина", ["alina"] = "Алина", ["diana"] = "Диана",
        ["kseniya"] = "Ксения", ["ksenia"] = "Ксения", ["xenia"] = "Ксения",
        ["sofia"] = "София", ["sofiya"] = "София", ["veronika"] = "Вероника",
        ["viktoria"] = "Виктория", ["viktoriya"] = "Виктория",
        ["evgeniya"] = "Евгения", ["zinaida"] = "Зинаида", ["lyubov"] = "Любовь",
        ["raisa"] = "Раиса", ["alla"] = "Алла", ["albina"] = "Альбина",

        // Surnames
        ["ivanov"] = "Иванов", ["ivanova"] = "Иванова",
        ["petrov"] = "Петров", ["petrova"] = "Петрова",
        ["sidorov"] = "Сидоров", ["sidorova"] = "Сидорова",
        ["smirnov"] = "Смирнов", ["smirnova"] = "Смирнова",
        ["kuznetsov"] = "Кузнецов", ["kuznetsova"] = "Кузнецова",
        ["popov"] = "Попов", ["popova"] = "Попова",
        ["sokolov"] = "Соколов", ["sokolova"] = "Соколова",
        ["lebedev"] = "Лебедев", ["lebedeva"] = "Лебедева",
        ["kozlov"] = "Козлов", ["kozlova"] = "Козлова",
        ["novikov"] = "Новиков", ["novikova"] = "Новикова",
        ["morozov"] = "Морозов", ["morozova"] = "Морозова",
        ["volkov"] = "Волков", ["volkova"] = "Волкова",
        ["alekseev"] = "Алексеев", ["alekseeva"] = "Алексеева",
        ["fedorov"] = "Фёдоров", ["fedorova"] = "Фёдорова",
        ["mikhailov"] = "Михайлов", ["mikhailova"] = "Михайлова",
        ["belov"] = "Белов", ["belova"] = "Белова",
        ["makarov"] = "Макаров", ["makarova"] = "Макарова",
        ["kovalev"] = "Ковалёв", ["kovaleva"] = "Ковалёва",
    };

    public WrongAlphabetDetector(ILogger logger)
    {
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Result of wrong alphabet detection
    /// </summary>
    public record DetectionResult(
        string Name,
        NameOrigin Origin,
        NameScript ActualScript,
        float Confidence,
        string? SuggestedCorrection,
        string Reason);

    /// <summary>
    /// Origin/native language of the name
    /// </summary>
    public enum NameOrigin
    {
        Unknown,
        Russian,        // Name is Russian (should be in Cyrillic)
        English,        // Name is English (should be in Latin)
        International   // Name works in both languages (e.g., Anna, Maria)
    }

    /// <summary>
    /// Detect if a name is written in the wrong alphabet
    /// </summary>
    public DetectionResult Detect(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new DetectionResult(name, NameOrigin.Unknown, NameScript.Unknown, 0, null, "Empty name");

        var script = DetectScript(name);

        return script switch
        {
            NameScript.Latin => DetectRussianInLatin(name),
            NameScript.Cyrillic => DetectEnglishInCyrillic(name),
            _ => new DetectionResult(name, NameOrigin.Unknown, script, 0, null, "Mixed or unknown script")
        };
    }

    /// <summary>
    /// Detect if a Latin-script name is actually Russian (transliterated)
    /// </summary>
    private DetectionResult DetectRussianInLatin(string name)
    {
        var nameLower = name.ToLowerInvariant();

        // 1. Check known Russian names dictionary with Cyrillic mapping
        if (RussianNamesLatinToCyrillic.TryGetValue(nameLower, out var cyrillicVersion))
        {
            return new DetectionResult(
                name,
                NameOrigin.Russian,
                NameScript.Latin,
                0.95f,
                cyrillicVersion,
                "Known Russian name in Latin script");
        }

        // 1b. Check known Russian names without mapping
        if (KnownRussianNames.Contains(nameLower))
        {
            return new DetectionResult(
                name,
                NameOrigin.Russian,
                NameScript.Latin,
                0.90f,
                null,
                "Known Russian name in Latin script");
        }

        // 2. Check if it's a known English name (then it's correct)
        if (KnownEnglishNames.Contains(nameLower))
        {
            return new DetectionResult(
                name,
                NameOrigin.English,
                NameScript.Latin,
                0.90f,
                null,
                "Known English name in correct script");
        }

        // 3. Check transliteration patterns
        var patternScore = CalculateRussianTranslitScore(nameLower);
        if (patternScore > 0.6f)
        {
            return new DetectionResult(
                name,
                NameOrigin.Russian,
                NameScript.Latin,
                patternScore,
                null,
                "Russian transliteration patterns detected");
        }

        // 4. Use ML model if trained
        if (_predictionEngine != null)
        {
            var prediction = _predictionEngine.Predict(new NameScriptInput { Name = name });
            if (prediction.PredictedLabel == "ru-translit" && prediction.Score?.Max() > 0.7f)
            {
                return new DetectionResult(
                    name,
                    NameOrigin.Russian,
                    NameScript.Latin,
                    prediction.Score.Max(),
                    null,
                    "ML model prediction");
            }
        }

        return new DetectionResult(
            name,
            NameOrigin.Unknown,
            NameScript.Latin,
            0.5f,
            null,
            "Could not determine origin");
    }

    /// <summary>
    /// Detect if a Cyrillic-script name is actually English (phonetic transcription)
    /// </summary>
    private DetectionResult DetectEnglishInCyrillic(string name)
    {
        var nameLower = name.ToLowerInvariant();

        // 1. Check known English names in Cyrillic dictionary
        if (EnglishNamesCyrillic.TryGetValue(nameLower, out var englishVersion))
        {
            return new DetectionResult(
                name,
                NameOrigin.English,
                NameScript.Cyrillic,
                0.95f,
                englishVersion,
                "Known English name in Cyrillic script");
        }

        // 2. Check English phonetic patterns in Cyrillic
        var patternScore = CalculateEnglishCyrillicScore(nameLower);
        if (patternScore > 0.6f)
        {
            return new DetectionResult(
                name,
                NameOrigin.English,
                NameScript.Cyrillic,
                patternScore,
                null,
                "English phonetic patterns in Cyrillic detected");
        }

        // 3. Use ML model if trained
        if (_predictionEngine != null)
        {
            var prediction = _predictionEngine.Predict(new NameScriptInput { Name = name });
            if (prediction.PredictedLabel == "en-cyrillic" && prediction.Score?.Max() > 0.7f)
            {
                return new DetectionResult(
                    name,
                    NameOrigin.English,
                    NameScript.Cyrillic,
                    prediction.Score.Max(),
                    null,
                    "ML model prediction");
            }
        }

        // Default: assume it's a Russian name in correct script
        return new DetectionResult(
            name,
            NameOrigin.Russian,
            NameScript.Cyrillic,
            0.7f,
            null,
            "Assumed native Russian name");
    }

    /// <summary>
    /// Calculate score for Russian transliteration patterns
    /// </summary>
    private float CalculateRussianTranslitScore(string name)
    {
        var score = 0f;
        var matchedPatterns = new List<string>();

        foreach (var pattern in RussianTranslitPatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                matchedPatterns.Add(pattern);
                // Weight different patterns
                score += pattern.Length switch
                {
                    >= 4 => 0.3f,  // Long patterns like "ovich", "skaya"
                    3 => 0.2f,     // Medium patterns like "sky", "ova"
                    _ => 0.1f      // Short patterns like "zh", "kh"
                };
            }
        }

        // Cap at 0.9
        return Math.Min(score, 0.9f);
    }

    /// <summary>
    /// Calculate score for English patterns in Cyrillic
    /// </summary>
    private float CalculateEnglishCyrillicScore(string name)
    {
        var score = 0f;

        foreach (var pattern in EnglishCyrillicPatterns)
        {
            if (name.Contains(pattern))
            {
                score += 0.2f;
            }
        }

        // "дж" at the start is very indicative of English J
        if (name.StartsWith("дж"))
        {
            score += 0.3f;
        }

        return Math.Min(score, 0.9f);
    }

    private NameScript DetectScript(string text)
    {
        var latinCount = 0;
        var cyrillicCount = 0;

        foreach (var c in text)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                (c >= 0x00C0 && c <= 0x024F))
                latinCount++;
            else if (c >= 0x0400 && c <= 0x04FF)
                cyrillicCount++;
        }

        if (latinCount > 0 && cyrillicCount > 0) return NameScript.Mixed;
        if (latinCount > 0) return NameScript.Latin;
        if (cyrillicCount > 0) return NameScript.Cyrillic;
        return NameScript.Unknown;
    }

    #region ML Training

    /// <summary>
    /// Train the ML model on labeled data
    /// </summary>
    public void Train(IEnumerable<NameScriptTrainingData> trainingData)
    {
        var dataList = trainingData.ToList();
        if (dataList.Count == 0)
        {
            _logger.LogWarning("No training data provided");
            return;
        }

        _logger.LogInformation("Training wrong alphabet detector with {Count} samples", dataList.Count);

        var data = _mlContext.Data.LoadFromEnumerable(dataList);

        var pipeline = _mlContext.Transforms.Conversion
            .MapValueToKey("Label", nameof(NameScriptTrainingData.Category))
            .Append(_mlContext.Transforms.Text.FeaturizeText(
                "NameFeatures",
                new Microsoft.ML.Transforms.Text.TextFeaturizingEstimator.Options
                {
                    CharFeatureExtractor = new Microsoft.ML.Transforms.Text.WordBagEstimator.Options
                    {
                        NgramLength = 3,
                        UseAllLengths = true
                    },
                    WordFeatureExtractor = null
                },
                nameof(NameScriptTrainingData.Name)))
            .Append(_mlContext.Transforms.Concatenate("Features", "NameFeatures"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                "PredictedLabel", "PredictedLabel"));

        _model = pipeline.Fit(data);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<NameScriptInput, NameScriptPrediction>(_model);

        _logger.LogInformation("Model trained successfully");
    }

    /// <summary>
    /// Generate training data from known names
    /// </summary>
    public IEnumerable<NameScriptTrainingData> GenerateTrainingData()
    {
        // Russian names in correct Cyrillic script
        var russianCyrillic = new[]
        {
            "Иван", "Дмитрий", "Сергей", "Александр", "Алексей", "Андрей", "Михаил", "Николай",
            "Владимир", "Виктор", "Юрий", "Павел", "Олег", "Игорь", "Борис", "Василий",
            "Анна", "Мария", "Елена", "Ольга", "Наталья", "Ирина", "Татьяна", "Екатерина",
            "Светлана", "Людмила", "Галина", "Нина", "Валентина", "Вера", "Надежда",
            "Иванов", "Петров", "Сидоров", "Смирнов", "Кузнецов", "Попов", "Соколов",
            "Лебедев", "Козлов", "Новиков", "Морозов", "Волков", "Алексеев", "Федоров"
        };

        foreach (var name in russianCyrillic)
            yield return new NameScriptTrainingData { Name = name, Category = "ru-native" };

        // Russian names in Latin (transliterated)
        foreach (var name in KnownRussianNames.Take(100))
            yield return new NameScriptTrainingData { Name = name, Category = "ru-translit" };

        // English names in correct Latin script
        foreach (var name in KnownEnglishNames.Take(100))
            yield return new NameScriptTrainingData { Name = name, Category = "en-native" };

        // English names in Cyrillic
        foreach (var name in EnglishNamesCyrillic.Keys)
            yield return new NameScriptTrainingData { Name = name, Category = "en-cyrillic" };
    }

    #endregion
}

/// <summary>
/// Input for wrong alphabet prediction
/// </summary>
public class NameScriptInput
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Prediction result
/// </summary>
public class NameScriptPrediction
{
    public string PredictedLabel { get; set; } = string.Empty;
    public float[]? Score { get; set; }
}

/// <summary>
/// Training data for wrong alphabet detection
/// Categories: ru-native, ru-translit, en-native, en-cyrillic
/// </summary>
public class NameScriptTrainingData
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
