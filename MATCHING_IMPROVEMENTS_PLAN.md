# План улучшения алгоритмов сопоставления GedSync

## Содержание

1. [Обзор текущей системы](#1-обзор-текущей-системы)
2. [Улучшение 1: Женские формы фамилий](#2-улучшение-1-женские-формы-фамилий)
3. [Улучшение 2: Расширенный словарь имён](#3-улучшение-2-расширенный-словарь-имён)
4. [Улучшение 3: Географическое сопоставление](#4-улучшение-3-географическое-сопоставление)
5. [Улучшение 4: Двунаправленная транслитерация](#5-улучшение-4-двунаправленная-транслитерация)
6. [Улучшение 5: Контекстные семейные бонусы](#6-улучшение-5-контекстные-семейные-бонусы)
7. [Улучшение 6: Многопроходное сопоставление](#7-улучшение-6-многопроходное-сопоставление)
8. [Улучшение 7: Улучшенная обработка дат](#8-улучшение-7-улучшенная-обработка-дат)
9. [Улучшение 8: Фонетическое сопоставление](#9-улучшение-8-фонетическое-сопоставление)
10. [Улучшение 9: Машинное обучение](#10-улучшение-9-машинное-обучение)
11. [План реализации по фазам](#11-план-реализации-по-фазам)
12. [Готовые библиотеки и решения](#12-готовые-библиотеки-и-решения)

---

## 1. Обзор текущей системы

### 1.1 Архитектура сопоставления

```
┌─────────────────────────────────────────────────────────────────┐
│                      WaveCompareService                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  BFS от якоря → расширение по семьям → сопоставление    │    │
│  └─────────────────────────────────────────────────────────┘    │
│                              │                                   │
│                              ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                   FuzzyMatcherService                    │    │
│  │  ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌──────────┐ │    │
│  │  │ Имена     │ │ Даты      │ │ Места     │ │ Семья    │ │    │
│  │  │ (25 pts)  │ │ (15 pts)  │ │ (10 pts)  │ │ (25 pts) │ │    │
│  │  └───────────┘ └───────────┘ └───────────┘ └──────────┘ │    │
│  └─────────────────────────────────────────────────────────┘    │
│                              │                                   │
│                              ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                  NameVariantsService                     │    │
│  │  • Транслитерация кириллица→латиница                    │    │
│  │  • ~30 вариантов славянских имён                        │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Текущие ограничения

| Область | Ограничение | Влияние |
|---------|-------------|---------|
| Фамилии | Нет обработки женских форм (-ова, -ская) | Высокое |
| Имена | Только 30 вариантов в словаре | Среднее |
| Места | Только токенный Jaccard | Высокое |
| Транслитерация | Только в одну сторону | Среднее |
| Даты | Жёсткий порог 15 лет | Среднее |
| Контекст | Слабое использование семейного контекста | Высокое |

---

## 2. Улучшение 1: Женские формы фамилий

### 2.1 Проблема

```
Источник (MyHeritage):  "Мария Иванова"
Назначение (Geni):      "Мария Иванов"
Текущий результат:      Фамилии не совпадают (Jaro-Winkler ~0.85)
Ожидаемый результат:    Фамилии совпадают (1.0)
```

### 2.2 Решение: SurnameNormalizer

**Файл:** `GedcomGeniSync.Core/Services/SurnameNormalizer.cs`

```csharp
namespace GedcomGeniSync.Core.Services;

/// <summary>
/// Normalizes surnames to base (masculine) form for comparison.
/// Handles Slavic surname patterns (Russian, Ukrainian, Polish, etc.)
/// </summary>
public class SurnameNormalizer
{
    /// <summary>
    /// Feminine surname endings mapped to their masculine equivalents.
    /// Order matters: longer suffixes must come before shorter ones.
    /// </summary>
    private static readonly (string Feminine, string Masculine)[] SlavicSuffixes = new[]
    {
        // Russian/Ukrainian adjective-based surnames
        ("ская", "ский"),   // Чайковская → Чайковский
        ("цкая", "цкий"),   // Троцкая → Троцкий
        ("ная", "ный"),     // Красная → Красный
        ("ая", "ий"),       // Горькая → Горький (adjectives)

        // Standard Russian patronymic-style
        ("ова", "ов"),      // Иванова → Иванов
        ("ева", "ев"),      // Медведева → Медведев
        ("ёва", "ёв"),      // Королёва → Королёв
        ("ина", "ин"),      // Путина → Путин
        ("ына", "ын"),      // Лисицына → Лисицын

        // Ukrainian surnames
        ("енко", "енко"),   // Unchanged: Шевченко
        ("ук", "ук"),       // Unchanged: Полищук
        ("юк", "юк"),       // Unchanged: Ковалюк

        // Polish surnames
        ("ska", "ski"),     // Kowalska → Kowalski
        ("cka", "cki"),     // Nowicka → Nowicki
        ("dzka", "dzki"),   // Zawadzka → Zawadzki
        ("na", "ny"),       // Czerwona → Czerwony (adjectives)

        // Latin transliterated forms
        ("ova", "ov"),      // Ivanova → Ivanov
        ("eva", "ev"),      // Medvedeva → Medvedev
        ("ina", "in"),      // Putina → Putin
        ("aya", "iy"),      // Gorskaya → Gorskiy
        ("skaya", "skiy"),  // Chaikovskaya → Chaikovskiy
    };

    /// <summary>
    /// Surnames that should not be modified (they look feminine but aren't).
    /// </summary>
    private static readonly HashSet<string> Exceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Names ending in -а/-я that are not feminine forms
        "Сковорода", "Skovoroda",
        "Кочерга", "Kocherga",
        "Лоза", "Loza",
        "Гроза", "Groza",
        "Сирота", "Sirota",
        // Ukrainian surnames unchanged for both genders
        "Шевченко", "Shevchenko",
        "Бондаренко", "Bondarenko",
        "Коваленко", "Kovalenko",
        "Ткаченко", "Tkachenko",
        // Georgian surnames
        "Саакашвили", "Saakashvili",
        "Джугашвили", "Dzhugashvili",
    };

    /// <summary>
    /// Normalizes a surname to its base (masculine) form.
    /// </summary>
    /// <param name="surname">The surname to normalize</param>
    /// <returns>Normalized surname in masculine form</returns>
    public string Normalize(string? surname)
    {
        if (string.IsNullOrWhiteSpace(surname))
            return string.Empty;

        var trimmed = surname.Trim();

        // Check exceptions first
        if (Exceptions.Contains(trimmed))
            return trimmed;

        // Try each suffix replacement
        foreach (var (feminine, masculine) in SlavicSuffixes)
        {
            if (trimmed.EndsWith(feminine, StringComparison.OrdinalIgnoreCase))
            {
                // Don't change if feminine == masculine (Ukrainian surnames)
                if (feminine.Equals(masculine, StringComparison.OrdinalIgnoreCase))
                    return trimmed;

                // Replace suffix
                var baseName = trimmed[..^feminine.Length];
                return baseName + masculine;
            }
        }

        // No matching suffix found, return as-is
        return trimmed;
    }

    /// <summary>
    /// Compares two surnames accounting for gender variations.
    /// </summary>
    /// <param name="surname1">First surname</param>
    /// <param name="surname2">Second surname</param>
    /// <returns>True if surnames match (ignoring gender suffix)</returns>
    public bool AreEquivalent(string? surname1, string? surname2)
    {
        if (string.IsNullOrWhiteSpace(surname1) && string.IsNullOrWhiteSpace(surname2))
            return true;

        if (string.IsNullOrWhiteSpace(surname1) || string.IsNullOrWhiteSpace(surname2))
            return false;

        var normalized1 = Normalize(surname1);
        var normalized2 = Normalize(surname2);

        return normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns similarity score between two surnames (0.0 - 1.0).
    /// Returns 1.0 for equivalent surnames, falls back to Jaro-Winkler otherwise.
    /// </summary>
    public double GetSimilarity(string? surname1, string? surname2,
        Func<string, string, double> fallbackSimilarity)
    {
        if (AreEquivalent(surname1, surname2))
            return 1.0;

        // Normalize both and try again
        var norm1 = Normalize(surname1);
        var norm2 = Normalize(surname2);

        if (norm1.Equals(norm2, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Use fallback (Jaro-Winkler) for non-matching surnames
        return fallbackSimilarity(norm1, norm2);
    }
}
```

### 2.3 Интеграция в FuzzyMatcherService

**Изменения в:** `GedcomGeniSync.Core/Services/FuzzyMatcherService.cs`

```csharp
// Добавить поле
private readonly SurnameNormalizer _surnameNormalizer = new();

// Изменить метод CompareLastNames
private double CompareLastNames(string? lastName1, string? lastName2)
{
    if (string.IsNullOrWhiteSpace(lastName1) && string.IsNullOrWhiteSpace(lastName2))
        return 1.0;

    if (string.IsNullOrWhiteSpace(lastName1) || string.IsNullOrWhiteSpace(lastName2))
        return 0.3; // One missing

    // NEW: Use surname normalizer for gender-aware comparison
    return _surnameNormalizer.GetSimilarity(
        lastName1,
        lastName2,
        (s1, s2) => _jaroWinkler.Similarity(s1.ToLowerInvariant(), s2.ToLowerInvariant()));
}
```

### 2.4 Тесты

**Файл:** `GedcomGeniSync.Tests/Services/SurnameNormalizerTests.cs`

```csharp
public class SurnameNormalizerTests
{
    private readonly SurnameNormalizer _normalizer = new();

    [Theory]
    [InlineData("Иванова", "Иванов")]
    [InlineData("Петрова", "Петров")]
    [InlineData("Сидорова", "Сидоров")]
    [InlineData("Медведева", "Медведев")]
    [InlineData("Чайковская", "Чайковский")]
    [InlineData("Kowalska", "Kowalski")]
    [InlineData("Ivanova", "Ivanov")]
    public void Normalize_FeminineSurname_ReturnsMasculine(string input, string expected)
    {
        Assert.Equal(expected, _normalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Шевченко", "Шевченко")]  // Ukrainian - unchanged
    [InlineData("Бондаренко", "Бондаренко")]
    [InlineData("Сковорода", "Сковорода")]  // Exception
    public void Normalize_UnchangedSurnames_ReturnsOriginal(string input, string expected)
    {
        Assert.Equal(expected, _normalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Иванова", "Иванов", true)]
    [InlineData("Петров", "Петров", true)]
    [InlineData("Чайковская", "Чайковский", true)]
    [InlineData("Иванов", "Петров", false)]
    public void AreEquivalent_VariousPairs_ReturnsExpected(
        string s1, string s2, bool expected)
    {
        Assert.Equal(expected, _normalizer.AreEquivalent(s1, s2));
    }
}
```

---

## 3. Улучшение 2: Расширенный словарь имён

### 3.1 Проблема

Текущий словарь содержит ~30 имён. Для качественного сопоставления нужно:
- 500+ базовых имён
- Уменьшительные формы (Александр → Саша, Шура, Алекс)
- Региональные варианты (Иван = Ivan = John = Johann = Jan = Giovanni)

### 3.2 Структура расширенного словаря

**Файл:** `GedcomGeniSync.Core/Data/name_variants.csv`

```csv
# Canonical,Variant1,Variant2,Variant3,...
# Мужские имена
Александр,Саша,Шура,Алекс,Alexander,Alex,Sasha,Shura,Oleksandr,Аляксандр
Алексей,Лёша,Алёша,Alexey,Alexei,Aleksei,Alyosha,Lesha
Андрей,Andrey,Andrei,Andrew,Andriy,Андрій
Анатолий,Толя,Anatoly,Anatoliy,Anatoli
Борис,Боря,Boris,Borya
Вадим,Vadim,Vadym
Валентин,Валя,Valentin,Valentine,Valentyn
Валерий,Валера,Valery,Valeriy,Valera
Василий,Вася,Vasily,Vasiliy,Vasil,Basil,Vasya
Виктор,Витя,Victor,Viktor,Vitya
Виталий,Vitaly,Vitaliy,Vitalii
Владимир,Вова,Володя,Vladimir,Volodymyr,Uladzimir,Vova,Volodya,Wladimir
Владислав,Влад,Vladislav,Vlad,Vladyslav
Вячеслав,Слава,Vyacheslav,Slava,Viacheslav
Геннадий,Гена,Gennady,Gennadiy,Gena
Георгий,Жора,Гоша,Georgy,Georgiy,George,Yuri,Zhora,Gosha
Григорий,Гриша,Grigory,Grigoriy,Gregory,Grisha
Даниил,Даня,Daniel,Daniil,Danil,Danya
Дмитрий,Дима,Dmitry,Dmitriy,Dmitri,Dima,Dmytro
Евгений,Женя,Evgeny,Evgeniy,Eugene,Yevgeny,Zhenya
Иван,Ваня,Ivan,Vanya,John,Johann,Jan,Giovanni,Jean,Juan,Iwan
Игорь,Igor,Ihor
Илья,Ilya,Ilia,Elijah,Ilia
Кирилл,Kirill,Cyril,Kyrylo
Константин,Костя,Konstantin,Constantine,Kostya,Kostyantyn
Леонид,Лёня,Leonid,Lenya
Максим,Макс,Maxim,Maksim,Max
Михаил,Миша,Mikhail,Michail,Michael,Misha,Mykhailo,Michal
Никита,Nikita
Николай,Коля,Nikolay,Nikolai,Nicholas,Nick,Kolya,Mykola
Олег,Oleg,Oleh
Павел,Паша,Pavel,Paul,Pasha,Pavlo
Пётр,Петя,Petr,Peter,Pyotr,Petya,Petro,Piotr
Роман,Рома,Roman,Roma
Сергей,Серёжа,Sergey,Sergei,Serge,Seryozha,Serhiy
Станислав,Стас,Stanislav,Stas
Степан,Стёпа,Stepan,Stephen,Steven,Styopa
Тимофей,Тима,Timofey,Timothy,Tima
Фёдор,Федя,Fedor,Fyodor,Theodore,Fedya
Юрий,Юра,Yury,Yuriy,Yuri,George,Yura

# Женские имена
Александра,Саша,Шура,Alexandra,Aleksandra,Sasha,Shura,Oleksandra
Алина,Alina,Aline
Анастасия,Настя,Anastasia,Nastya,Anastasiya
Анна,Аня,Anna,Anne,Ann,Anya,Hanna,Hannah
Валентина,Валя,Valentina,Valya
Вера,Vera
Виктория,Вика,Victoria,Vika,Viktoriya
Галина,Галя,Galina,Galya
Дарья,Даша,Daria,Darya,Dasha
Евгения,Женя,Evgenia,Yevgenia,Zhenya
Екатерина,Катя,Ekaterina,Katerina,Catherine,Kate,Katya
Елена,Лена,Elena,Helena,Helen,Lena,Olena
Елизавета,Лиза,Elizaveta,Elizabeth,Liza,Lisa
Ирина,Ира,Irina,Irene,Ira
Ксения,Ксюша,Ksenia,Kseniya,Xenia,Ksyusha
Лариса,Лара,Larisa,Lara
Людмила,Люда,Lyudmila,Ludmila,Lyuda,Lyudmyla
Мария,Маша,Maria,Mary,Marie,Masha,Mariya
Марина,Marina
Надежда,Надя,Nadezhda,Nadya,Hope
Наталья,Наташа,Natalia,Natasha,Natalya,Nataliya
Нина,Nina
Оксана,Oksana,Oxana
Ольга,Оля,Olga,Olya
Светлана,Света,Svetlana,Sveta
София,Соня,Sofia,Sophia,Sonya,Sofiya
Татьяна,Таня,Tatiana,Tatyana,Tanya
Юлия,Юля,Julia,Yulia,Yuliya,Julya
```

### 3.3 Улучшенный NameVariantsService

**Файл:** `GedcomGeniSync.Core/Services/NameVariantsService.cs`

```csharp
public class NameVariantsService : INameVariantsService
{
    private readonly Dictionary<string, HashSet<string>> _variantGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameToCanonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<NameVariantsService> _logger;

    public NameVariantsService(ILogger<NameVariantsService> logger)
    {
        _logger = logger;
        LoadBuiltInVariants();
    }

    /// <summary>
    /// Loads the extended name variants from embedded resource or file.
    /// </summary>
    private void LoadBuiltInVariants()
    {
        // Load from embedded CSV resource
        var assembly = typeof(NameVariantsService).Assembly;
        var resourceName = "GedcomGeniSync.Core.Data.name_variants.csv";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _logger.LogWarning("Could not load embedded name variants resource. Using minimal built-in list.");
            LoadMinimalBuiltInVariants();
            return;
        }

        using var reader = new StreamReader(stream);
        LoadFromReader(reader);

        _logger.LogInformation("Loaded {Count} canonical names with variants", _variantGroups.Count);
    }

    private void LoadFromReader(TextReader reader)
    {
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var parts = line.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (parts.Count < 2)
            {
                _logger.LogDebug("Skipping line {Line}: insufficient variants", lineNumber);
                continue;
            }

            var canonical = parts[0];
            var variants = new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);

            // Store group
            _variantGroups[canonical] = variants;

            // Map each variant to canonical
            foreach (var variant in variants)
            {
                _nameToCanonical[variant] = canonical;
            }
        }
    }

    /// <summary>
    /// Checks if two names are variants of each other.
    /// </summary>
    public bool AreVariants(string? name1, string? name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return false;

        // Direct match
        if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if both map to the same canonical name
        var canonical1 = GetCanonicalName(name1);
        var canonical2 = GetCanonicalName(name2);

        if (canonical1 != null && canonical2 != null)
            return canonical1.Equals(canonical2, StringComparison.OrdinalIgnoreCase);

        // Check if one is in the other's variant group
        if (_variantGroups.TryGetValue(name1, out var group1) && group1.Contains(name2))
            return true;
        if (_variantGroups.TryGetValue(name2, out var group2) && group2.Contains(name1))
            return true;

        return false;
    }

    /// <summary>
    /// Gets the canonical (standard) form of a name.
    /// </summary>
    public string? GetCanonicalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _nameToCanonical.TryGetValue(name, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// Gets all known variants for a name.
    /// </summary>
    public IReadOnlySet<string> GetVariants(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new HashSet<string>();

        // First check if this name is a canonical name
        if (_variantGroups.TryGetValue(name, out var directVariants))
            return directVariants;

        // Check if this name maps to a canonical name
        if (_nameToCanonical.TryGetValue(name, out var canonical)
            && _variantGroups.TryGetValue(canonical, out var canonicalVariants))
            return canonicalVariants;

        return new HashSet<string>();
    }

    /// <summary>
    /// Returns similarity score between names considering variants.
    /// </summary>
    public double GetSimilarity(string? name1, string? name2)
    {
        if (string.IsNullOrWhiteSpace(name1) && string.IsNullOrWhiteSpace(name2))
            return 1.0;

        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0.0;

        // Exact match
        if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Known variants
        if (AreVariants(name1, name2))
            return 0.95; // Slightly lower than exact match

        // Not variants
        return 0.0;
    }
}
```

### 3.4 Добавление встроенного ресурса

В файле проекта `GedcomGeniSync.Core.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data\name_variants.csv" />
</ItemGroup>
```

---

## 4. Улучшение 3: Географическое сопоставление

### 4.1 Проблема

```
Источник:    "Москва, Россия"
Назначение:  "Moscow, Russia"
Текущий:     Jaccard на токенах ≈ 0.0 (разные слова)
Ожидаемый:   1.0 (одно и то же место)
```

### 4.2 Решение: PlaceNormalizer с иерархией

**Файл:** `GedcomGeniSync.Core/Services/PlaceNormalizer.cs`

```csharp
namespace GedcomGeniSync.Core.Services;

/// <summary>
/// Normalizes and compares place names with support for:
/// - Transliteration (Москва ↔ Moscow)
/// - Historical names (Ленинград = Санкт-Петербург)
/// - Hierarchical matching (город ⊂ область ⊂ страна)
/// </summary>
public class PlaceNormalizer
{
    /// <summary>
    /// Place name synonyms: canonical form → all known variants.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> PlaceSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Russian cities
        ["Москва"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Москва", "Moscow", "Moskva", "Moskau", "Moscou", "Mosca"
        },
        ["Санкт-Петербург"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Санкт-Петербург", "Saint Petersburg", "St. Petersburg", "St Petersburg",
            "Petersburg", "Петербург", "Ленинград", "Leningrad", "Петроград", "Petrograd",
            "Sankt-Peterburg", "SPb"
        },
        ["Киев"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Киев", "Kiev", "Kyiv", "Kijów", "Kiew"
        },
        ["Одесса"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Одесса", "Odessa", "Odesa"
        },
        ["Харьков"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Харьков", "Kharkov", "Kharkiv", "Charkow"
        },
        ["Минск"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Минск", "Minsk", "Miensk"
        },
        ["Варшава"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Варшава", "Warsaw", "Warszawa", "Warschau"
        },
        ["Вильнюс"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Вильнюс", "Vilnius", "Wilno", "Vilna", "Вильно", "Вильна"
        },
        ["Рига"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Рига", "Riga"
        },
        ["Таллин"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Таллин", "Tallinn", "Таллинн", "Reval", "Ревель"
        },

        // Countries
        ["Россия"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Россия", "Russia", "Russian Federation", "USSR", "СССР",
            "Soviet Union", "Советский Союз", "Russian Empire", "Российская Империя",
            "РСФСР", "RSFSR", "РФ"
        },
        ["Украина"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Украина", "Ukraine", "Ukrayina", "Ukrainian SSR", "УССР"
        },
        ["Беларусь"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Беларусь", "Belarus", "Byelorussia", "Белоруссия",
            "Belorussia", "БССР", "Byelorussian SSR"
        },
        ["Польша"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Польша", "Poland", "Polska", "Polen"
        },
        ["Германия"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Германия", "Germany", "Deutschland", "German Empire",
            "Германская Империя", "ФРГ", "ГДР", "FRG", "GDR", "DDR", "BRD"
        },
        ["США"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "США", "US", "USA", "United States", "United States of America",
            "America", "Америка", "Соединённые Штаты"
        },
        ["Израиль"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Израиль", "Israel", "ישראל"
        },
    };

    /// <summary>
    /// Reverse lookup: any variant → canonical name.
    /// </summary>
    private static readonly Dictionary<string, string> VariantToCanonical;

    static PlaceNormalizer()
    {
        VariantToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, variants) in PlaceSynonyms)
        {
            foreach (var variant in variants)
            {
                VariantToCanonical[variant] = canonical;
            }
        }
    }

    /// <summary>
    /// Parses a place string into hierarchical components.
    /// "Москва, Московская область, Россия" → ["Москва", "Московская область", "Россия"]
    /// </summary>
    public List<string> ParseComponents(string? place)
    {
        if (string.IsNullOrWhiteSpace(place))
            return new List<string>();

        return place
            .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    /// <summary>
    /// Normalizes a single place component to its canonical form.
    /// </summary>
    public string NormalizeComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
            return string.Empty;

        var trimmed = component.Trim();

        // Check if it's a known variant
        if (VariantToCanonical.TryGetValue(trimmed, out var canonical))
            return canonical;

        return trimmed;
    }

    /// <summary>
    /// Compares two place names and returns similarity (0.0 - 1.0).
    /// </summary>
    public double Compare(string? place1, string? place2)
    {
        if (string.IsNullOrWhiteSpace(place1) && string.IsNullOrWhiteSpace(place2))
            return 1.0;

        if (string.IsNullOrWhiteSpace(place1) || string.IsNullOrWhiteSpace(place2))
            return 0.0;

        // Parse into components
        var components1 = ParseComponents(place1).Select(NormalizeComponent).ToList();
        var components2 = ParseComponents(place2).Select(NormalizeComponent).ToList();

        if (components1.Count == 0 && components2.Count == 0)
            return 1.0;

        if (components1.Count == 0 || components2.Count == 0)
            return 0.0;

        // Calculate match score
        var matches = 0;
        var totalWeight = 0.0;

        // City/locality (first component) has highest weight
        if (components1.Count > 0 && components2.Count > 0)
        {
            totalWeight += 0.5;
            if (ComponentsMatch(components1[0], components2[0]))
                matches += 50;
        }

        // Region (second component if exists)
        if (components1.Count > 1 && components2.Count > 1)
        {
            totalWeight += 0.3;
            if (ComponentsMatch(components1[1], components2[1]))
                matches += 30;
        }

        // Country (last component)
        var country1 = components1.Count > 0 ? components1[^1] : null;
        var country2 = components2.Count > 0 ? components2[^1] : null;
        if (!string.IsNullOrEmpty(country1) && !string.IsNullOrEmpty(country2))
        {
            totalWeight += 0.2;
            if (ComponentsMatch(country1, country2))
                matches += 20;
        }

        // Partial containment bonus
        // If one is fully contained in the other, give bonus
        var set1 = new HashSet<string>(components1, StringComparer.OrdinalIgnoreCase);
        var set2 = new HashSet<string>(components2, StringComparer.OrdinalIgnoreCase);

        if (set1.IsSubsetOf(set2) || set2.IsSubsetOf(set1))
        {
            matches += 10; // Containment bonus
        }

        return Math.Min(1.0, matches / 100.0);
    }

    /// <summary>
    /// Checks if two normalized components are equivalent.
    /// </summary>
    private bool ComponentsMatch(string? comp1, string? comp2)
    {
        if (string.IsNullOrWhiteSpace(comp1) || string.IsNullOrWhiteSpace(comp2))
            return false;

        // Direct match
        if (comp1.Equals(comp2, StringComparison.OrdinalIgnoreCase))
            return true;

        // Both normalize to the same canonical
        var norm1 = NormalizeComponent(comp1);
        var norm2 = NormalizeComponent(comp2);

        return norm1.Equals(norm2, StringComparison.OrdinalIgnoreCase);
    }
}
```

### 4.3 Интеграция в FuzzyMatcherService

```csharp
private readonly PlaceNormalizer _placeNormalizer = new();

private double CompareBirthPlaces(string? place1, string? place2)
{
    return _placeNormalizer.Compare(place1, place2);
}
```

---

## 5. Улучшение 4: Двунаправленная транслитерация

### 5.1 Проблема

```
Geni (латиница):     "Vladimir Ivanovich Petrov"
MyHeritage (кирил):  "Владимир Иванович Петров"
Текущий:             Только кириллица → латиница
Нужно:               Латиница → кириллица для обратного сравнения
```

### 5.2 Решение: Расширенный TransliterationService

**Файл:** `GedcomGeniSync.Core/Services/TransliterationService.cs`

```csharp
namespace GedcomGeniSync.Core.Services;

/// <summary>
/// Bidirectional transliteration between Cyrillic and Latin scripts.
/// Supports Russian, Ukrainian, and Belarusian transliteration standards.
/// </summary>
public class TransliterationService
{
    /// <summary>
    /// Cyrillic to Latin mapping (Russian GOST 7.79-2000 System B base).
    /// </summary>
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        // Russian
        ['А'] = "A", ['а'] = "a",
        ['Б'] = "B", ['б'] = "b",
        ['В'] = "V", ['в'] = "v",
        ['Г'] = "G", ['г'] = "g",
        ['Д'] = "D", ['д'] = "d",
        ['Е'] = "E", ['е'] = "e",
        ['Ё'] = "Yo", ['ё'] = "yo",
        ['Ж'] = "Zh", ['ж'] = "zh",
        ['З'] = "Z", ['з'] = "z",
        ['И'] = "I", ['и'] = "i",
        ['Й'] = "Y", ['й'] = "y",
        ['К'] = "K", ['к'] = "k",
        ['Л'] = "L", ['л'] = "l",
        ['М'] = "M", ['м'] = "m",
        ['Н'] = "N", ['н'] = "n",
        ['О'] = "O", ['о'] = "o",
        ['П'] = "P", ['п'] = "p",
        ['Р'] = "R", ['р'] = "r",
        ['С'] = "S", ['с'] = "s",
        ['Т'] = "T", ['т'] = "t",
        ['У'] = "U", ['у'] = "u",
        ['Ф'] = "F", ['ф'] = "f",
        ['Х'] = "Kh", ['х'] = "kh",
        ['Ц'] = "Ts", ['ц'] = "ts",
        ['Ч'] = "Ch", ['ч'] = "ch",
        ['Ш'] = "Sh", ['ш'] = "sh",
        ['Щ'] = "Shch", ['щ'] = "shch",
        ['Ъ'] = "", ['ъ'] = "",
        ['Ы'] = "Y", ['ы'] = "y",
        ['Ь'] = "", ['ь'] = "",
        ['Э'] = "E", ['э'] = "e",
        ['Ю'] = "Yu", ['ю'] = "yu",
        ['Я'] = "Ya", ['я'] = "ya",

        // Ukrainian specific
        ['Є'] = "Ye", ['є'] = "ye",
        ['І'] = "I", ['і'] = "i",
        ['Ї'] = "Yi", ['ї'] = "yi",
        ['Ґ'] = "G", ['ґ'] = "g",

        // Belarusian specific
        ['Ў'] = "U", ['ў'] = "u",
    };

    /// <summary>
    /// Latin to Cyrillic multi-character sequences (order matters: longer first).
    /// </summary>
    private static readonly (string Latin, string Cyrillic)[] LatinToCyrillicSequences = new[]
    {
        // 4-character
        ("shch", "щ"), ("Shch", "Щ"), ("SHCH", "Щ"),

        // 3-character
        ("sch", "щ"), ("Sch", "Щ"), ("SCH", "Щ"),  // German-style

        // 2-character
        ("zh", "ж"), ("Zh", "Ж"), ("ZH", "Ж"),
        ("kh", "х"), ("Kh", "Х"), ("KH", "Х"),
        ("ts", "ц"), ("Ts", "Ц"), ("TS", "Ц"),
        ("ch", "ч"), ("Ch", "Ч"), ("CH", "Ч"),
        ("sh", "ш"), ("Sh", "Ш"), ("SH", "Ш"),
        ("yu", "ю"), ("Yu", "Ю"), ("YU", "Ю"),
        ("ya", "я"), ("Ya", "Я"), ("YA", "Я"),
        ("yo", "ё"), ("Yo", "Ё"), ("YO", "Ё"),
        ("ye", "е"), ("Ye", "Е"), ("YE", "Е"),
        ("yi", "ї"), ("Yi", "Ї"), ("YI", "Ї"),
        ("iy", "ий"), ("Iy", "Ий"), ("IY", "ИЙ"),
        ("ey", "ей"), ("Ey", "Ей"), ("EY", "ЕЙ"),
        ("ay", "ай"), ("Ay", "Ай"), ("AY", "АЙ"),
        ("oy", "ой"), ("Oy", "Ой"), ("OY", "ОЙ"),
        ("uy", "уй"), ("Uy", "Уй"), ("UY", "УЙ"),
    };

    /// <summary>
    /// Single Latin to Cyrillic character mapping.
    /// </summary>
    private static readonly Dictionary<char, char> LatinToCyrillicSingle = new()
    {
        ['A'] = 'А', ['a'] = 'а',
        ['B'] = 'Б', ['b'] = 'б',
        ['V'] = 'В', ['v'] = 'в',
        ['G'] = 'Г', ['g'] = 'г',
        ['D'] = 'Д', ['d'] = 'д',
        ['E'] = 'Е', ['e'] = 'е',
        ['Z'] = 'З', ['z'] = 'з',
        ['I'] = 'И', ['i'] = 'и',
        ['Y'] = 'Й', ['y'] = 'й',  // Default, context-dependent
        ['K'] = 'К', ['k'] = 'к',
        ['L'] = 'Л', ['l'] = 'л',
        ['M'] = 'М', ['m'] = 'м',
        ['N'] = 'Н', ['n'] = 'н',
        ['O'] = 'О', ['o'] = 'о',
        ['P'] = 'П', ['p'] = 'п',
        ['R'] = 'Р', ['r'] = 'р',
        ['S'] = 'С', ['s'] = 'с',
        ['T'] = 'Т', ['t'] = 'т',
        ['U'] = 'У', ['u'] = 'у',
        ['F'] = 'Ф', ['f'] = 'ф',
    };

    /// <summary>
    /// Transliterates Cyrillic text to Latin.
    /// </summary>
    public string CyrillicToLatinText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = new StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            if (CyrillicToLatin.TryGetValue(c, out var latin))
                result.Append(latin);
            else
                result.Append(c); // Keep non-Cyrillic as-is
        }

        return result.ToString();
    }

    /// <summary>
    /// Transliterates Latin text to Cyrillic (Russian).
    /// </summary>
    public string LatinToCyrillicText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var matched = false;

            // Try multi-character sequences first (longest first)
            foreach (var (latin, cyrillic) in LatinToCyrillicSequences)
            {
                if (i + latin.Length <= text.Length &&
                    text.Substring(i, latin.Length).Equals(latin, StringComparison.Ordinal))
                {
                    result.Append(cyrillic);
                    i += latin.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                // Try single character
                var c = text[i];
                if (LatinToCyrillicSingle.TryGetValue(c, out var cyrillic))
                    result.Append(cyrillic);
                else
                    result.Append(c); // Keep non-Latin as-is

                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks if text is primarily Cyrillic.
    /// </summary>
    public bool IsCyrillic(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var cyrillicCount = text.Count(c => c >= 0x0400 && c <= 0x04FF);
        var letterCount = text.Count(char.IsLetter);

        return letterCount > 0 && (double)cyrillicCount / letterCount > 0.5;
    }

    /// <summary>
    /// Checks if text is primarily Latin.
    /// </summary>
    public bool IsLatin(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var latinCount = text.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
        var letterCount = text.Count(char.IsLetter);

        return letterCount > 0 && (double)latinCount / letterCount > 0.5;
    }

    /// <summary>
    /// Normalizes text to a common form for comparison.
    /// Converts everything to Latin lowercase.
    /// </summary>
    public string NormalizeForComparison(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // If Cyrillic, transliterate to Latin
        var normalized = IsCyrillic(text) ? CyrillicToLatinText(text) : text;

        return normalized.ToLowerInvariant();
    }
}
```

### 5.3 Интеграция

```csharp
// В FuzzyMatcherService
private readonly TransliterationService _transliteration = new();

private double CompareNames(string? name1, string? name2)
{
    // Normalize both to Latin lowercase
    var norm1 = _transliteration.NormalizeForComparison(name1);
    var norm2 = _transliteration.NormalizeForComparison(name2);

    // Now compare normalized forms
    return _jaroWinkler.Similarity(norm1, norm2);
}
```

---

## 6. Улучшение 5: Контекстные семейные бонусы

### 6.1 Проблема

Текущая система недостаточно использует контекст уже сопоставленных родственников.

### 6.2 Решение: FamilyContextBonus

**Файл:** `GedcomGeniSync.Core/Services/Wave/FamilyContextScorer.cs`

```csharp
namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Calculates bonus scores based on already-matched family members.
/// The more relatives are matched, the more confident we are about a candidate.
/// </summary>
public class FamilyContextScorer
{
    /// <summary>
    /// Bonus points when both parents are already matched.
    /// </summary>
    public const int BothParentsMatchedBonus = 15;

    /// <summary>
    /// Bonus points when one parent is already matched.
    /// </summary>
    public const int OneParentMatchedBonus = 8;

    /// <summary>
    /// Bonus points when spouse is already matched.
    /// </summary>
    public const int SpouseMatchedBonus = 10;

    /// <summary>
    /// Bonus points per matched sibling (capped).
    /// </summary>
    public const int PerSiblingBonus = 4;

    /// <summary>
    /// Maximum bonus from siblings.
    /// </summary>
    public const int MaxSiblingBonus = 16;

    /// <summary>
    /// Bonus points per matched child (capped).
    /// </summary>
    public const int PerChildBonus = 3;

    /// <summary>
    /// Maximum bonus from children.
    /// </summary>
    public const int MaxChildrenBonus = 12;

    /// <summary>
    /// Calculates the context bonus for a candidate match.
    /// </summary>
    /// <param name="sourcePerson">Person from source GEDCOM</param>
    /// <param name="destPerson">Candidate person from destination</param>
    /// <param name="existingMappings">Already established mappings (sourceId → destId)</param>
    /// <param name="sourcePersons">All persons in source GEDCOM</param>
    /// <param name="destPersons">All persons in destination</param>
    /// <returns>Bonus points to add to the match score (0-50 range)</returns>
    public int CalculateContextBonus(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        IReadOnlyDictionary<string, string> existingMappings,
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons)
    {
        var bonus = 0;

        // 1. Parent bonus
        bonus += CalculateParentBonus(sourcePerson, destPerson, existingMappings);

        // 2. Spouse bonus
        bonus += CalculateSpouseBonus(sourcePerson, destPerson, existingMappings);

        // 3. Sibling bonus
        bonus += CalculateSiblingBonus(sourcePerson, destPerson, existingMappings, sourcePersons, destPersons);

        // 4. Children bonus
        bonus += CalculateChildrenBonus(sourcePerson, destPerson, existingMappings);

        // 5. Consistency bonus: if family structure is consistent
        bonus += CalculateConsistencyBonus(sourcePerson, destPerson);

        return Math.Min(bonus, 50); // Cap at 50 points
    }

    private int CalculateParentBonus(
        PersonRecord source,
        PersonRecord dest,
        IReadOnlyDictionary<string, string> mappings)
    {
        var fatherMatched = !string.IsNullOrEmpty(source.FatherId)
            && mappings.TryGetValue(source.FatherId, out var mappedFatherId)
            && mappedFatherId == dest.FatherId;

        var motherMatched = !string.IsNullOrEmpty(source.MotherId)
            && mappings.TryGetValue(source.MotherId, out var mappedMotherId)
            && mappedMotherId == dest.MotherId;

        if (fatherMatched && motherMatched)
            return BothParentsMatchedBonus;

        if (fatherMatched || motherMatched)
            return OneParentMatchedBonus;

        return 0;
    }

    private int CalculateSpouseBonus(
        PersonRecord source,
        PersonRecord dest,
        IReadOnlyDictionary<string, string> mappings)
    {
        foreach (var sourceSpouseId in source.SpouseIds)
        {
            if (mappings.TryGetValue(sourceSpouseId, out var mappedSpouseId))
            {
                if (dest.SpouseIds.Contains(mappedSpouseId))
                    return SpouseMatchedBonus;
            }
        }

        return 0;
    }

    private int CalculateSiblingBonus(
        PersonRecord source,
        PersonRecord dest,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons)
    {
        var matchedSiblings = 0;

        foreach (var sourceSiblingId in source.SiblingIds)
        {
            if (mappings.TryGetValue(sourceSiblingId, out var mappedSiblingId))
            {
                if (dest.SiblingIds.Contains(mappedSiblingId))
                    matchedSiblings++;
            }
        }

        return Math.Min(matchedSiblings * PerSiblingBonus, MaxSiblingBonus);
    }

    private int CalculateChildrenBonus(
        PersonRecord source,
        PersonRecord dest,
        IReadOnlyDictionary<string, string> mappings)
    {
        var matchedChildren = 0;

        foreach (var sourceChildId in source.ChildrenIds)
        {
            if (mappings.TryGetValue(sourceChildId, out var mappedChildId))
            {
                if (dest.ChildrenIds.Contains(mappedChildId))
                    matchedChildren++;
            }
        }

        return Math.Min(matchedChildren * PerChildBonus, MaxChildrenBonus);
    }

    private int CalculateConsistencyBonus(PersonRecord source, PersonRecord dest)
    {
        var bonus = 0;

        // Gender consistency (should always match for good candidates)
        if (source.Gender == dest.Gender && source.Gender != GedcomGender.Unknown)
            bonus += 2;

        // Birth year consistency with parents (if available)
        // Child should be born after parents were at least 15 years old
        // This is a soft bonus for biologically consistent data

        return bonus;
    }
}
```

### 6.3 Интеграция в FuzzyMatcherService

```csharp
// Добавить поле
private readonly FamilyContextScorer _contextScorer = new();

// Изменить основной метод сопоставления
public MatchResult CalculateMatchScore(
    PersonRecord source,
    PersonRecord dest,
    IReadOnlyDictionary<string, string>? existingMappings = null)
{
    // ... существующий код расчёта baseScore ...

    var baseScore = CalculateBaseScore(source, dest);

    // Apply context bonus if mappings are available
    var contextBonus = 0;
    if (existingMappings != null && existingMappings.Count > 0)
    {
        contextBonus = _contextScorer.CalculateContextBonus(
            source, dest, existingMappings, _sourcePersons, _destPersons);
    }

    var finalScore = Math.Min(100, baseScore + contextBonus);

    return new MatchResult
    {
        Score = finalScore,
        BaseScore = baseScore,
        ContextBonus = contextBonus,
        // ... другие поля ...
    };
}
```

---

## 7. Улучшение 6: Многопроходное сопоставление

### 7.1 Концепция

```
Проход 1: Высокая уверенность (score ≥ 85)
    ├── RFN совпадение
    ├── Точное совпадение имени + даты рождения + пола
    └── Результат: Надёжные якоря для следующих проходов

Проход 2: Средняя уверенность (score 60-84)
    ├── Используем контекст от Прохода 1
    ├── Если родители сопоставлены → понижаем threshold для детей
    └── Результат: Расширенное множество сопоставлений

Проход 3: Низкая уверенность (score 40-59)
    ├── Максимальное использование контекста
    ├── Предлагаем пользователю для подтверждения
    └── Результат: Кандидаты для ручной проверки
```

### 7.2 Реализация: MultiPassMatcher

**Файл:** `GedcomGeniSync.Core/Services/Wave/MultiPassMatcher.cs`

```csharp
namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Implements multi-pass matching strategy for improved accuracy.
/// </summary>
public class MultiPassMatcher
{
    private readonly IFuzzyMatcherService _fuzzyMatcher;
    private readonly FamilyContextScorer _contextScorer;
    private readonly ILogger<MultiPassMatcher> _logger;

    /// <summary>
    /// Configuration for each matching pass.
    /// </summary>
    public class PassConfig
    {
        public int PassNumber { get; init; }
        public string Name { get; init; } = "";
        public int MinScore { get; init; }
        public int MaxScore { get; init; } = 100;
        public bool UseContextBonus { get; init; }
        public bool RequireUserConfirmation { get; init; }
        public int ThresholdReductionForChildrenWithParents { get; init; }
    }

    private static readonly PassConfig[] Passes = new[]
    {
        new PassConfig
        {
            PassNumber = 1,
            Name = "High Confidence",
            MinScore = 85,
            UseContextBonus = false, // Don't need bonus for high confidence
            RequireUserConfirmation = false,
            ThresholdReductionForChildrenWithParents = 0
        },
        new PassConfig
        {
            PassNumber = 2,
            Name = "Medium Confidence",
            MinScore = 60,
            MaxScore = 84,
            UseContextBonus = true,
            RequireUserConfirmation = false,
            ThresholdReductionForChildrenWithParents = 15 // Lower threshold if parents matched
        },
        new PassConfig
        {
            PassNumber = 3,
            Name = "Low Confidence (Review)",
            MinScore = 40,
            MaxScore = 59,
            UseContextBonus = true,
            RequireUserConfirmation = true,
            ThresholdReductionForChildrenWithParents = 20
        }
    };

    public MultiPassMatcher(
        IFuzzyMatcherService fuzzyMatcher,
        FamilyContextScorer contextScorer,
        ILogger<MultiPassMatcher> logger)
    {
        _fuzzyMatcher = fuzzyMatcher;
        _contextScorer = contextScorer;
        _logger = logger;
    }

    /// <summary>
    /// Executes multi-pass matching and returns all mappings.
    /// </summary>
    public async Task<MultiPassResult> ExecuteAsync(
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons,
        string anchorSourceId,
        string anchorDestId,
        IUserConfirmationService? confirmationService = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiPassResult();
        var mappings = new Dictionary<string, string>();
        var usedDestIds = new HashSet<string>();

        // Initialize with anchor
        mappings[anchorSourceId] = anchorDestId;
        usedDestIds.Add(anchorDestId);
        result.Mappings.Add(new PersonMapping
        {
            SourceId = anchorSourceId,
            DestinationId = anchorDestId,
            MatchScore = 100,
            MatchedBy = "Anchor",
            Pass = 0
        });

        _logger.LogInformation("Starting multi-pass matching with anchor {AnchorSource} → {AnchorDest}",
            anchorSourceId, anchorDestId);

        foreach (var pass in Passes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("=== Pass {Pass}: {Name} (threshold {Min}-{Max}) ===",
                pass.PassNumber, pass.Name, pass.MinScore, pass.MaxScore);

            var passResult = await ExecutePassAsync(
                pass,
                sourcePersons,
                destPersons,
                mappings,
                usedDestIds,
                confirmationService,
                cancellationToken);

            // Add new mappings
            foreach (var mapping in passResult.NewMappings)
            {
                mappings[mapping.SourceId] = mapping.DestinationId;
                usedDestIds.Add(mapping.DestinationId);
                result.Mappings.Add(mapping);
            }

            result.PassResults.Add(passResult);

            _logger.LogInformation("Pass {Pass} completed: {NewMappings} new mappings, {Total} total",
                pass.PassNumber, passResult.NewMappings.Count, mappings.Count);
        }

        result.TotalMappings = mappings.Count;
        return result;
    }

    private async Task<PassResult> ExecutePassAsync(
        PassConfig pass,
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons,
        Dictionary<string, string> existingMappings,
        HashSet<string> usedDestIds,
        IUserConfirmationService? confirmationService,
        CancellationToken cancellationToken)
    {
        var passResult = new PassResult { PassNumber = pass.PassNumber };
        var candidates = new List<(string SourceId, string DestId, int Score, int ContextBonus)>();

        // Find candidates for unmapped source persons
        foreach (var (sourceId, sourcePerson) in sourcePersons)
        {
            if (existingMappings.ContainsKey(sourceId))
                continue; // Already mapped

            // Calculate effective threshold for this person
            var effectiveThreshold = CalculateEffectiveThreshold(
                pass, sourcePerson, existingMappings);

            // Find best candidate in destination
            foreach (var (destId, destPerson) in destPersons)
            {
                if (usedDestIds.Contains(destId))
                    continue; // Already used

                var baseScore = _fuzzyMatcher.CalculateMatchScore(sourcePerson, destPerson);

                var contextBonus = pass.UseContextBonus
                    ? _contextScorer.CalculateContextBonus(
                        sourcePerson, destPerson, existingMappings, sourcePersons, destPersons)
                    : 0;

                var totalScore = Math.Min(100, baseScore + contextBonus);

                if (totalScore >= effectiveThreshold && totalScore <= pass.MaxScore)
                {
                    candidates.Add((sourceId, destId, totalScore, contextBonus));
                }
            }
        }

        // Sort by score descending
        candidates = candidates.OrderByDescending(c => c.Score).ToList();

        // Process candidates (greedy best-first)
        var processedSources = new HashSet<string>();
        var processedDests = new HashSet<string>();

        foreach (var (sourceId, destId, score, contextBonus) in candidates)
        {
            if (processedSources.Contains(sourceId) || processedDests.Contains(destId))
                continue;

            // User confirmation if required
            if (pass.RequireUserConfirmation && confirmationService != null)
            {
                var sourcePerson = sourcePersons[sourceId];
                var destPerson = destPersons[destId];

                var confirmed = await confirmationService.ConfirmMatchAsync(
                    sourcePerson, destPerson, score, contextBonus, cancellationToken);

                if (!confirmed)
                {
                    passResult.Skipped++;
                    continue;
                }
            }

            processedSources.Add(sourceId);
            processedDests.Add(destId);

            passResult.NewMappings.Add(new PersonMapping
            {
                SourceId = sourceId,
                DestinationId = destId,
                MatchScore = score,
                ContextBonus = contextBonus,
                MatchedBy = pass.Name,
                Pass = pass.PassNumber
            });
        }

        return passResult;
    }

    private int CalculateEffectiveThreshold(
        PassConfig pass,
        PersonRecord sourcePerson,
        Dictionary<string, string> existingMappings)
    {
        var threshold = pass.MinScore;

        // If both parents are already mapped, reduce threshold
        var fatherMapped = !string.IsNullOrEmpty(sourcePerson.FatherId)
            && existingMappings.ContainsKey(sourcePerson.FatherId);
        var motherMapped = !string.IsNullOrEmpty(sourcePerson.MotherId)
            && existingMappings.ContainsKey(sourcePerson.MotherId);

        if (fatherMapped && motherMapped)
        {
            threshold -= pass.ThresholdReductionForChildrenWithParents;
            threshold = Math.Max(30, threshold); // Never go below 30
        }
        else if (fatherMapped || motherMapped)
        {
            threshold -= pass.ThresholdReductionForChildrenWithParents / 2;
            threshold = Math.Max(35, threshold);
        }

        return threshold;
    }
}

public class MultiPassResult
{
    public List<PersonMapping> Mappings { get; } = new();
    public List<PassResult> PassResults { get; } = new();
    public int TotalMappings { get; set; }
}

public class PassResult
{
    public int PassNumber { get; set; }
    public List<PersonMapping> NewMappings { get; } = new();
    public int Skipped { get; set; }
}

public class PersonMapping
{
    public required string SourceId { get; init; }
    public required string DestinationId { get; init; }
    public int MatchScore { get; init; }
    public int ContextBonus { get; init; }
    public required string MatchedBy { get; init; }
    public int Pass { get; init; }
}
```

---

## 8. Улучшение 7: Улучшенная обработка дат

### 8.1 Проблема

- Жёсткий порог в 15 лет
- Нет обработки неполных дат (только год, ABT, BEF, AFT)

### 8.2 Решение: DateComparer

**Файл:** `GedcomGeniSync.Core/Services/DateComparer.cs`

```csharp
namespace GedcomGeniSync.Core.Services;

/// <summary>
/// Advanced date comparison with support for:
/// - Approximate dates (ABT, EST, CAL)
/// - Range dates (BET, AND)
/// - Before/after dates (BEF, AFT)
/// - Partial dates (only year, year+month)
/// - Soft penalty for differences
/// </summary>
public class DateComparer
{
    /// <summary>
    /// Date precision levels.
    /// </summary>
    public enum DatePrecision
    {
        Unknown = 0,
        Decade = 1,      // ~1930s
        Year = 2,        // 1934
        YearMonth = 3,   // JUL 1934
        Full = 4         // 14 JUL 1934
    }

    /// <summary>
    /// Date qualifier affecting certainty.
    /// </summary>
    public enum DateQualifier
    {
        Exact,           // No qualifier
        About,           // ABT, EST, CAL
        Before,          // BEF
        After,           // AFT
        Between          // BET...AND
    }

    /// <summary>
    /// Parsed date with metadata.
    /// </summary>
    public class ParsedDate
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public DatePrecision Precision { get; set; }
        public DateQualifier Qualifier { get; set; }
        public int? YearRangeEnd { get; set; } // For BET...AND
    }

    /// <summary>
    /// Parses a GEDCOM date string.
    /// </summary>
    public ParsedDate Parse(string? dateStr)
    {
        var result = new ParsedDate { Qualifier = DateQualifier.Exact };

        if (string.IsNullOrWhiteSpace(dateStr))
            return result;

        var text = dateStr.Trim().ToUpperInvariant();

        // Check qualifiers
        if (text.StartsWith("ABT ") || text.StartsWith("EST ") || text.StartsWith("CAL "))
        {
            result.Qualifier = DateQualifier.About;
            text = text[4..].Trim();
        }
        else if (text.StartsWith("BEF "))
        {
            result.Qualifier = DateQualifier.Before;
            text = text[4..].Trim();
        }
        else if (text.StartsWith("AFT "))
        {
            result.Qualifier = DateQualifier.After;
            text = text[4..].Trim();
        }
        else if (text.StartsWith("BET "))
        {
            result.Qualifier = DateQualifier.Between;
            var andIndex = text.IndexOf(" AND ", StringComparison.Ordinal);
            if (andIndex > 0)
            {
                var startPart = text[4..andIndex].Trim();
                var endPart = text[(andIndex + 5)..].Trim();

                // Parse start year
                if (int.TryParse(ExtractYear(startPart), out var startYear))
                    result.Year = startYear;
                if (int.TryParse(ExtractYear(endPart), out var endYear))
                    result.YearRangeEnd = endYear;

                result.Precision = DatePrecision.Year;
                return result;
            }
        }

        // Parse the date parts
        var parts = text.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (TryParseYear(part, out var year))
            {
                result.Year = year;
                result.Precision = DatePrecision.Year;
            }
            else if (TryParseMonth(part, out var month))
            {
                result.Month = month;
                if (result.Year.HasValue)
                    result.Precision = DatePrecision.YearMonth;
            }
            else if (TryParseDay(part, out var day))
            {
                result.Day = day;
                if (result.Year.HasValue && result.Month.HasValue)
                    result.Precision = DatePrecision.Full;
            }
        }

        return result;
    }

    /// <summary>
    /// Compares two dates and returns similarity score (0.0 - 1.0).
    /// </summary>
    public double Compare(string? date1, string? date2)
    {
        var parsed1 = Parse(date1);
        var parsed2 = Parse(date2);

        return Compare(parsed1, parsed2);
    }

    /// <summary>
    /// Compares two parsed dates.
    /// </summary>
    public double Compare(ParsedDate date1, ParsedDate date2)
    {
        // Both unknown
        if (!date1.Year.HasValue && !date2.Year.HasValue)
            return 0.5; // Neutral

        // One unknown
        if (!date1.Year.HasValue || !date2.Year.HasValue)
            return 0.3; // Slight penalty

        var year1 = date1.Year.Value;
        var year2 = date2.Year.Value;

        // Handle ranges
        if (date1.Qualifier == DateQualifier.Between && date1.YearRangeEnd.HasValue)
        {
            if (year2 >= year1 && year2 <= date1.YearRangeEnd.Value)
                return 0.9; // Within range
        }
        if (date2.Qualifier == DateQualifier.Between && date2.YearRangeEnd.HasValue)
        {
            if (year1 >= year2 && year1 <= date2.YearRangeEnd.Value)
                return 0.9;
        }

        // Handle before/after
        if (date1.Qualifier == DateQualifier.Before && year2 < year1)
            return 0.85;
        if (date1.Qualifier == DateQualifier.After && year2 > year1)
            return 0.85;
        if (date2.Qualifier == DateQualifier.Before && year1 < year2)
            return 0.85;
        if (date2.Qualifier == DateQualifier.After && year1 > year2)
            return 0.85;

        // Calculate year difference
        var yearDiff = Math.Abs(year1 - year2);
        var yearScore = CalculateYearScore(yearDiff);

        // Apply precision bonus if years match and we have more precision
        if (yearDiff == 0)
        {
            if (date1.Month.HasValue && date2.Month.HasValue)
            {
                if (date1.Month == date2.Month)
                {
                    yearScore = Math.Max(yearScore, 0.95);

                    if (date1.Day.HasValue && date2.Day.HasValue && date1.Day == date2.Day)
                        yearScore = 1.0;
                }
                else
                {
                    yearScore = Math.Max(yearScore, 0.92); // Same year, different month
                }
            }
        }

        // Apply qualifier uncertainty
        if (date1.Qualifier == DateQualifier.About || date2.Qualifier == DateQualifier.About)
        {
            // For approximate dates, be more lenient
            if (yearDiff <= 2)
                yearScore = Math.Max(yearScore, 0.85);
            else if (yearDiff <= 5)
                yearScore = Math.Max(yearScore, 0.7);
        }

        return yearScore;
    }

    /// <summary>
    /// Calculates score based on year difference with soft boundaries.
    /// </summary>
    private double CalculateYearScore(int yearDiff)
    {
        return yearDiff switch
        {
            0 => 1.0,
            1 => 0.95,
            2 => 0.88,
            3 => 0.78,
            4 => 0.68,
            5 => 0.58,
            <= 7 => 0.45,
            <= 10 => 0.30,
            <= 15 => 0.15,
            <= 20 => 0.05,
            _ => 0.0
        };
    }

    private bool TryParseYear(string part, out int year)
    {
        year = 0;
        if (part.Length == 4 && int.TryParse(part, out year))
            return year >= 1000 && year <= 2100;
        return false;
    }

    private bool TryParseMonth(string part, out int month)
    {
        month = 0;
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4,
            ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AUG"] = 8,
            ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12
        };

        return months.TryGetValue(part, out month);
    }

    private bool TryParseDay(string part, out int day)
    {
        day = 0;
        if (part.Length <= 2 && int.TryParse(part, out day))
            return day >= 1 && day <= 31;
        return false;
    }

    private string ExtractYear(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(\d{4})\b");
        return match.Success ? match.Groups[1].Value : "";
    }
}
```

---

## 9. Улучшение 8: Фонетическое сопоставление

### 9.1 Концепция

Фонетическое сопоставление помогает находить имена, которые звучат похоже, но пишутся по-разному.

### 9.2 Реализация: SlavicSoundex

**Файл:** `GedcomGeniSync.Core/Services/SlavicSoundex.cs`

```csharp
namespace GedcomGeniSync.Core.Services;

/// <summary>
/// Phonetic encoding for Slavic names.
/// Based on Soundex principles but adapted for Cyrillic/Latin Slavic names.
/// </summary>
public class SlavicSoundex
{
    /// <summary>
    /// Phonetic groups for Slavic sounds.
    /// </summary>
    private static readonly Dictionary<char, char> PhoneticMap = new()
    {
        // Vowels - all map to 0 (ignored after first letter)
        ['А'] = '0', ['Е'] = '0', ['Ё'] = '0', ['И'] = '0', ['О'] = '0',
        ['У'] = '0', ['Ы'] = '0', ['Э'] = '0', ['Ю'] = '0', ['Я'] = '0',
        ['A'] = '0', ['E'] = '0', ['I'] = '0', ['O'] = '0', ['U'] = '0', ['Y'] = '0',

        // Labials - B, P, V, F, M
        ['Б'] = '1', ['П'] = '1', ['В'] = '1', ['Ф'] = '1', ['М'] = '1',
        ['B'] = '1', ['P'] = '1', ['V'] = '1', ['F'] = '1', ['M'] = '1', ['W'] = '1',

        // Dentals - D, T, N
        ['Д'] = '2', ['Т'] = '2', ['Н'] = '2',
        ['D'] = '2', ['T'] = '2', ['N'] = '2',

        // Gutturals - G, K, H
        ['Г'] = '3', ['К'] = '3', ['Х'] = '3',
        ['G'] = '3', ['K'] = '3', ['H'] = '3', ['C'] = '3', ['Q'] = '3',

        // Sibilants - S, Z, Sh, Zh, Ch, Ts
        ['С'] = '4', ['З'] = '4', ['Ц'] = '4', ['Ш'] = '4', ['Щ'] = '4',
        ['Ж'] = '4', ['Ч'] = '4',
        ['S'] = '4', ['Z'] = '4', ['X'] = '4', ['J'] = '4',

        // Liquids - L, R
        ['Л'] = '5', ['Р'] = '5',
        ['L'] = '5', ['R'] = '5',

        // Semi-vowel
        ['Й'] = '6',
    };

    /// <summary>
    /// Generates phonetic code for a name.
    /// </summary>
    public string Encode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = name.ToUpperInvariant().Trim();
        var result = new StringBuilder(4);

        // Keep first letter as-is
        var firstChar = normalized[0];
        result.Append(char.ToUpper(firstChar));

        // Encode remaining characters
        var lastCode = GetPhoneticCode(firstChar);

        for (var i = 1; i < normalized.Length && result.Length < 4; i++)
        {
            var c = normalized[i];
            var code = GetPhoneticCode(c);

            // Skip vowels (0) and duplicate consecutive codes
            if (code != '0' && code != ' ' && code != lastCode)
            {
                result.Append(code);
                lastCode = code;
            }
            else if (code == '0')
            {
                // Vowel resets the duplicate detection
                lastCode = '0';
            }
        }

        // Pad with zeros to length 4
        while (result.Length < 4)
            result.Append('0');

        return result.ToString();
    }

    /// <summary>
    /// Compares two names phonetically.
    /// </summary>
    public bool AreSimilar(string? name1, string? name2)
    {
        var code1 = Encode(name1);
        var code2 = Encode(name2);

        return code1 == code2;
    }

    /// <summary>
    /// Returns phonetic similarity (0.0 - 1.0).
    /// </summary>
    public double GetSimilarity(string? name1, string? name2)
    {
        var code1 = Encode(name1);
        var code2 = Encode(name2);

        if (code1 == code2)
            return 1.0;

        if (string.IsNullOrEmpty(code1) || string.IsNullOrEmpty(code2))
            return 0.0;

        // Count matching positions
        var matches = 0;
        var minLen = Math.Min(code1.Length, code2.Length);

        for (var i = 0; i < minLen; i++)
        {
            if (code1[i] == code2[i])
                matches++;
        }

        return (double)matches / 4;
    }

    private char GetPhoneticCode(char c)
    {
        if (PhoneticMap.TryGetValue(c, out var code))
            return code;
        return ' '; // Unknown character
    }
}
```

---

## 10. Улучшение 9: Машинное обучение

### 10.1 Концепция

Собирать данные о подтверждённых и отклонённых сопоставлениях для корректировки весов.

### 10.2 Структура данных

**Файл:** `GedcomGeniSync.Core/Models/MatchingFeedback.cs`

```csharp
namespace GedcomGeniSync.Core.Models;

/// <summary>
/// Feedback data for machine learning improvements.
/// </summary>
public class MatchingFeedback
{
    /// <summary>
    /// Unique ID for this feedback entry.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the match was evaluated.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// User's decision: true = confirmed match, false = rejected.
    /// </summary>
    public bool IsMatch { get; init; }

    /// <summary>
    /// Original algorithm score (0-100).
    /// </summary>
    public int AlgorithmScore { get; init; }

    /// <summary>
    /// Feature vector for the match.
    /// </summary>
    public MatchFeatures Features { get; init; } = new();
}

/// <summary>
/// Feature vector extracted from a potential match.
/// </summary>
public class MatchFeatures
{
    // Name features
    public double FirstNameSimilarity { get; init; }
    public double LastNameSimilarity { get; init; }
    public double MiddleNameSimilarity { get; init; }
    public bool FirstNameExactMatch { get; init; }
    public bool LastNameExactMatch { get; init; }
    public bool AreNameVariants { get; init; }
    public bool SurnameGenderNormalized { get; init; }

    // Date features
    public double BirthDateSimilarity { get; init; }
    public double DeathDateSimilarity { get; init; }
    public int? BirthYearDifference { get; init; }
    public bool BothHaveBirthDate { get; init; }
    public bool BothHaveDeathDate { get; init; }

    // Place features
    public double BirthPlaceSimilarity { get; init; }
    public double DeathPlaceSimilarity { get; init; }

    // Family context features
    public bool FatherMatched { get; init; }
    public bool MotherMatched { get; init; }
    public bool BothParentsMatched { get; init; }
    public bool SpouseMatched { get; init; }
    public int MatchedSiblingsCount { get; init; }
    public int MatchedChildrenCount { get; init; }

    // Gender
    public bool GenderMatch { get; init; }
    public bool OneGenderUnknown { get; init; }
}

/// <summary>
/// Storage for matching feedback to enable ML improvements.
/// </summary>
public class MatchingFeedbackStore
{
    private readonly string _filePath;
    private readonly List<MatchingFeedback> _feedback = new();
    private readonly object _lock = new();

    public MatchingFeedbackStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public void AddFeedback(MatchingFeedback feedback)
    {
        lock (_lock)
        {
            _feedback.Add(feedback);
            Save();
        }
    }

    public IReadOnlyList<MatchingFeedback> GetAllFeedback() => _feedback.AsReadOnly();

    /// <summary>
    /// Calculates adjusted weights based on feedback.
    /// </summary>
    public Dictionary<string, double> CalculateOptimalWeights()
    {
        if (_feedback.Count < 50)
            return new Dictionary<string, double>(); // Not enough data

        // Simple approach: for each feature, calculate correlation with IsMatch
        var positives = _feedback.Where(f => f.IsMatch).ToList();
        var negatives = _feedback.Where(f => !f.IsMatch).ToList();

        var weights = new Dictionary<string, double>();

        // Compare average feature values between positives and negatives
        // Features with higher values in positives get higher weights

        // Example for FirstNameSimilarity
        var avgFirstNamePos = positives.Average(f => f.Features.FirstNameSimilarity);
        var avgFirstNameNeg = negatives.Average(f => f.Features.FirstNameSimilarity);
        weights["FirstName"] = Math.Max(0.1, Math.Min(0.5, (avgFirstNamePos - avgFirstNameNeg) * 0.5 + 0.25));

        // ... similar calculations for other features ...

        return weights;
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<List<MatchingFeedback>>(json);
            if (loaded != null)
                _feedback.AddRange(loaded);
        }
    }

    private void Save()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_feedback,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
```

---

## 11. План реализации по фазам

### Фаза A: Быстрые победы (1-2 дня)

| # | Улучшение | Файлы | Сложность |
|---|-----------|-------|-----------|
| 1 | Женские формы фамилий | SurnameNormalizer.cs | Низкая |
| 2 | Расширенный словарь имён | name_variants.csv, NameVariantsService.cs | Низкая |
| 3 | Улучшенная обработка дат | DateComparer.cs | Средняя |

### Фаза B: Географическое сопоставление (2-3 дня)

| # | Улучшение | Файлы | Сложность |
|---|-----------|-------|-----------|
| 4 | PlaceNormalizer с иерархией | PlaceNormalizer.cs | Средняя |
| 5 | Двунаправленная транслитерация | TransliterationService.cs | Средняя |

### Фаза C: Семейный контекст (3-4 дня)

| # | Улучшение | Файлы | Сложность |
|---|-----------|-------|-----------|
| 6 | FamilyContextScorer | FamilyContextScorer.cs | Средняя |
| 7 | Многопроходное сопоставление | MultiPassMatcher.cs | Высокая |

### Фаза D: Продвинутые функции (4-5 дней)

| # | Улучшение | Файлы | Сложность |
|---|-----------|-------|-----------|
| 8 | Фонетическое сопоставление | SlavicSoundex.cs | Средняя |
| 9 | ML обучение на обратной связи | MatchingFeedback*.cs | Высокая |

---

## 12. Готовые библиотеки и решения

### 12.1 Транслитерация (Улучшение 4)

| Пакет | Описание | Установка |
|-------|----------|-----------|
| **[NickBuhro.Translit](https://github.com/nick-buhro/Translit)** | GOST 7.79-2000 (ISO 9), двунаправленная, RU/UA/BY/BG/MK. 1000+ тестов, без зависимостей | `dotnet add package NickBuhro.Translit` |
| [Cyrillic.Convert](https://github.com/bajceticnenad/Cyrillic.Convert) | 10 языков включая грузинский, армянский, казахский | `dotnet add package Cyrillic.Convert` |

**Рекомендация:** `NickBuhro.Translit` - покрывает все славянские языки по стандарту GOST.

### 12.2 Фонетическое сопоставление (Улучшение 8)

| Пакет | Алгоритмы | Установка |
|-------|-----------|-----------|
| **[Lucene.Net.Analysis.Phonetic](https://www.nuget.org/packages/Lucene.Net.Analysis.Phonetic/)** | Soundex, Double Metaphone, **Beider-Morse**, Cologne, NYSIIS | `dotnet add package Lucene.Net.Analysis.Phonetic` |
| [Phonix](https://github.com/eldersantos/phonix) | Double Metaphone, Soundex, Caverphone, Match Rating | `dotnet add package Phonix` |
| [TwinFinder.Nuget](https://www.nuget.org/packages/TwinFinder.Nuget) | Фонетика + метрики (Levenshtein, Jaccard, Jaro-Winkler) | `dotnet add package TwinFinder.Nuget` |

**Рекомендация:** `Lucene.Net.Analysis.Phonetic` содержит **Beider-Morse** - алгоритм специально разработанный для славянских/еврейских имён. Он учитывает:
- Женские формы фамилий (Novikova ↔ Novikov)
- 16 языков включая русский, польский, чешский
- Final devoicing (озвончение в славянских языках)

### 12.3 Fuzzy-matching строк

| Пакет | Описание | Установка |
|-------|----------|-----------|
| **[FuzzySharp](https://github.com/JakeBayer/FuzzySharp)** | Порт FuzzyWuzzy, Ratio/PartialRatio/TokenSort | `dotnet add package FuzzySharp` |
| [FuzzyString](https://www.nuget.org/packages/FuzzyString) | 11 алгоритмов: Jaro-Winkler, Levenshtein, Jaccard и др. | `dotnet add package FuzzyString` |
| [String.Similarity](https://www.nuget.org/packages/String.Similarity) | Десяток алгоритмов сходства строк | `dotnet add package String.Similarity` |

### 12.4 Парсинг GEDCOM-дат (Улучшение 7)

**✅ Парсинг уже решён в проекте!**

Проект использует **[Gedcom.Net.SDK](https://www.nuget.org/packages/Gedcom.Net.SDK)** (Patagames), который полностью покрывает парсинг дат:

```csharp
using Patagames.GedcomNetSdk.Dates;

// Поддерживаемые типы дат в SDK:
// DateExact, Date           — точные даты
// DateAbout, DateEstimated  — ABT, EST
// DateCalculate             — CAL
// DateBefore, DateAfter     — BEF, AFT
// DateBetween, DateFromTo   — BET...AND, FROM...TO
// DatePhrase                — текстовые даты с интерпретацией
```

Парсинг уже работает в `GedcomLoader.ConvertDate()` — см. строки 1088-1241.

**Что нужно дописать:** только `DateComparer` — логику сравнения и расчёта similarity между двумя распарсенными датами.

| Источник | Описание | Статус |
|----------|----------|--------|
| **Gedcom.Net.SDK** | Парсинг всех типов GEDCOM-дат | ✅ Уже используется |
| [GeneGenie.Gedcom](https://github.com/TheGeneGenieProject/GeneGenie.Gedcom) | Референс для `CalculateSimilarityScore()` | AGPL-3.0 (только как референс) |

### 12.5 Географическое сопоставление (Улучшение 3)

| Источник | Описание |
|----------|----------|
| **[GeoNames API](https://www.geonames.org/)** | 25+ млн географических названий, fuzzy-поиск, иерархия мест |
| [GeoFinder](https://github.com/corb555/GeoFinder) | Специально для генеалогии - кладбища, исторические места |
| [Historic Gazetteer](https://gov.genealogy.net/) | Исторические немецкие названия мест (genealogy.net) |

### 12.6 Словарь имён (Улучшение 2)

| Источник | Описание |
|----------|----------|
| **[Behind the Name API](https://www.behindthename.com/api/)** | API для вариантов имён. В среднем 32 варианта на имя |
| [tfmorris/Names](https://github.com/tfmorris/Names) | Готовая база `givenname_behindthename.txt` с вариантами |
| [Wiktionary: Slavic surnames](https://en.wiktionary.org/wiki/Appendix:Slavic_surnames) | Кросс-ссылки между вариантами фамилий |

### 12.7 Машинное обучение (Улучшение 9)

| Пакет | Описание | Установка |
|-------|----------|-----------|
| **[ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet)** | Binary/Multiclass classification, AutoML | `dotnet add package Microsoft.ML` |
| [ML.NET Model Builder](https://marketplace.visualstudio.com/items?itemName=MLNET.07) | Visual Studio extension для AutoML | VS Extension |

### 12.8 Что использовать готовое vs писать самим

| Улучшение | Готовое решение | Писать самим |
|-----------|-----------------|--------------|
| 1. Женские формы фамилий | Beider-Morse в Lucene.Net | SurnameNormalizer (простой) |
| 2. Словарь имён | Behind the Name API / tfmorris/Names | Загрузчик CSV |
| 3. Географическое сопоставление | GeoNames API | PlaceNormalizer (обёртка) |
| 4. Транслитерация | **NickBuhro.Translit** | — |
| 5. Контекстные бонусы | — | FamilyContextScorer |
| 6. Многопроходное сопоставление | — | MultiPassMatcher |
| 7. Обработка дат | **Gedcom.Net.SDK** (парсинг ✅) | DateComparer (только сравнение) |
| 8. Фонетика | **Beider-Morse в Lucene.Net** | — |
| 9. ML | **ML.NET** | MatchingFeedbackStore |

### 12.9 Рекомендуемый минимальный набор пакетов

```bash
# Транслитерация - готовое решение
dotnet add package NickBuhro.Translit

# Фонетика с Beider-Morse для славянских имён
dotnet add package Lucene.Net.Analysis.Phonetic

# Fuzzy-matching
dotnet add package FuzzySharp

# ML (для фазы D)
dotnet add package Microsoft.ML
```

---

## Приложение: Метрики успеха

### До внедрения улучшений
- Процент автоматических сопоставлений: ~60%
- Ложноположительные: ~5%
- Ложноотрицательные: ~35%

### Ожидаемые результаты после всех улучшений
- Процент автоматических сопоставлений: ~85%
- Ложноположительные: ~2%
- Ложноотрицательные: ~13%

### Как измерять
1. Создать тестовый набор из 100+ известных сопоставлений
2. Прогнать алгоритм и посчитать precision/recall
3. Сравнить до и после каждого улучшения
