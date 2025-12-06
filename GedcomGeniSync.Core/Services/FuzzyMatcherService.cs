using System.Collections.Immutable;
using F23.StringSimilarity;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Fuzzy matching service for comparing PersonRecords
/// Uses Jaro-Winkler algorithm optimized for Slavic languages
/// </summary>
public class FuzzyMatcherService : IFuzzyMatcherService
{
    private readonly NameVariantsService _nameVariants;
    private readonly ILogger<FuzzyMatcherService> _logger;
    private readonly MatchingOptions _options;
    private readonly JaroWinkler _jaroWinkler;

    public FuzzyMatcherService(
        NameVariantsService nameVariants,
        ILogger<FuzzyMatcherService> logger,
        MatchingOptions? options = null)
    {
        _nameVariants = nameVariants;
        _logger = logger;
        _options = options ?? new MatchingOptions();
        _jaroWinkler = new JaroWinkler();

        // Validate and warn if weights are not normalized
        if (!_options.AreWeightsNormalized)
        {
            _logger.LogWarning(
                "Matching weights sum to {TotalWeight} instead of 100. " +
                "Scores will be automatically normalized using factor {NormalizationFactor:F2}. " +
                "Consider adjusting weights: FirstName={FirstName}, LastName={LastName}, " +
                "BirthDate={BirthDate}, BirthPlace={BirthPlace}, DeathDate={DeathDate}, Gender={Gender}",
                _options.TotalWeight,
                _options.NormalizationFactor,
                _options.FirstNameWeight,
                _options.LastNameWeight,
                _options.BirthDateWeight,
                _options.BirthPlaceWeight,
                _options.DeathDateWeight,
                _options.GenderWeight);
        }
    }

    /// <summary>
    /// Compare two persons and return match score (0-100)
    /// </summary>
    public MatchCandidate Compare(PersonRecord source, PersonRecord target)
    {
        var reasonsBuilder = ImmutableList.CreateBuilder<MatchReason>();

        // First name comparison
        var firstNameScore = CompareFirstNames(source, target);
        if (firstNameScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "FirstName",
                Points = firstNameScore * _options.FirstNameWeight,
                Details = $"{source.FirstName} ↔ {target.FirstName} ({firstNameScore:P0})"
            });
        }

        // Last name comparison
        var lastNameScore = CompareLastNames(source, target);
        if (lastNameScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "LastName",
                Points = lastNameScore * _options.LastNameWeight,
                Details = $"{source.LastName} ↔ {target.LastName} ({lastNameScore:P0})"
            });
        }

        // Birth date comparison
        var birthDateScore = CompareDates(source.BirthDate, target.BirthDate);
        if (birthDateScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "BirthDate",
                Points = birthDateScore * _options.BirthDateWeight,
                Details = $"{source.BirthDate} ↔ {target.BirthDate} ({birthDateScore:P0})"
            });
        }

        // Birth place comparison
        var birthPlaceScore = ComparePlaces(source.BirthPlace, target.BirthPlace);
        if (birthPlaceScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "BirthPlace",
                Points = birthPlaceScore * _options.BirthPlaceWeight,
                Details = $"{source.BirthPlace} ↔ {target.BirthPlace} ({birthPlaceScore:P0})"
            });
        }

        // Gender comparison (penalty for mismatch)
        var genderScore = CompareGender(source.Gender, target.Gender);
        if (genderScore < 1.0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "Gender",
                Points = (genderScore * _options.GenderWeight) - _options.GenderWeight,
                Details = $"{source.Gender} ↔ {target.Gender} (penalty)"
            });
        }

        // Death date comparison (bonus if both have it)
        var deathDateScore = CompareDates(source.DeathDate, target.DeathDate);
        if (deathDateScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "DeathDate",
                Points = deathDateScore * _options.DeathDateWeight,
                Details = $"{source.DeathDate} ↔ {target.DeathDate} ({deathDateScore:P0})"
            });
        }

        var reasons = reasonsBuilder.ToImmutable();
        var rawScore = reasons.Sum(r => r.Points);

        // Apply normalization if weights don't sum to 100
        var normalizedScore = rawScore * _options.NormalizationFactor;
        var score = Math.Min(100, Math.Max(0, normalizedScore));

        return new MatchCandidate
        {
            Source = source,
            Target = target,
            Score = score,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Find best matches for a person from a list of candidates
    /// </summary>
    public List<MatchCandidate> FindMatches(
        PersonRecord source, 
        IEnumerable<PersonRecord> candidates,
        int minScore = 0)
    {
        var matches = new List<MatchCandidate>();

        foreach (var candidate in candidates)
        {
            // Quick pre-filter: skip if gender definitely mismatches
            if (source.Gender != Gender.Unknown && 
                candidate.Gender != Gender.Unknown &&
                source.Gender != candidate.Gender)
            {
                continue;
            }

            // Quick pre-filter: skip if birth years are too far apart
            if (source.BirthYear.HasValue && candidate.BirthYear.HasValue)
            {
                var yearDiff = Math.Abs(source.BirthYear.Value - candidate.BirthYear.Value);
                if (yearDiff > _options.MaxBirthYearDifference)
                {
                    continue;
                }
            }

            var match = Compare(source, candidate);
            if (match.Score >= minScore)
            {
                matches.Add(match);
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    #region Name Comparison

    private double CompareFirstNames(PersonRecord source, PersonRecord target)
    {
        var sourceName = source.FirstName;
        var targetName = target.FirstName;

        if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(targetName))
            return 0;

        // 1. Exact match using pre-normalized names (optimization)
        if (!string.IsNullOrEmpty(source.NormalizedFirstName) &&
            !string.IsNullOrEmpty(target.NormalizedFirstName) &&
            source.NormalizedFirstName == target.NormalizedFirstName)
            return 1.0;

        // 2. Check name variants dictionary
        if (_nameVariants.AreEquivalent(sourceName, targetName))
            return 0.95;

        // 3. Check all name variants from both records
        var sourceVariants = GetAllNameVariants(source, true);
        var targetVariants = GetAllNameVariants(target, true);

        foreach (var sv in sourceVariants)
        {
            foreach (var tv in targetVariants)
            {
                if (_nameVariants.AreEquivalent(sv, tv))
                    return 0.90;
            }
        }

        // Use pre-normalized names if available, otherwise normalize on-the-fly
        var sourceNorm = source.NormalizedFirstName ?? NormalizeForComparison(sourceName);
        var targetNorm = target.NormalizedFirstName ?? NormalizeForComparison(targetName);

        // 4. Jaro-Winkler similarity (better for transliteration and Slavic languages)
        var similarity = _jaroWinkler.Similarity(sourceNorm, targetNorm);

        // 5. Check if one name is a substring of the other (for nicknames like Александр vs Саша)
        // This helps with diminutives common in Slavic languages
        var isSubstring = sourceNorm.Contains(targetNorm) || targetNorm.Contains(sourceNorm);
        if (isSubstring && similarity > 0.7)
        {
            similarity = Math.Max(similarity, 0.85);
        }

        return similarity;
    }

    private double CompareLastNames(PersonRecord source, PersonRecord target)
    {
        var sourceName = source.LastName;
        var targetName = target.LastName;

        if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(targetName))
            return 0;

        // 1. Exact match using pre-normalized names (optimization)
        if (!string.IsNullOrEmpty(source.NormalizedLastName) &&
            !string.IsNullOrEmpty(target.NormalizedLastName) &&
            source.NormalizedLastName == target.NormalizedLastName)
            return 1.0;

        // 2. Check maiden name
        if (!string.IsNullOrEmpty(source.MaidenName))
        {
            var normalizedMaiden = NormalizeForComparison(source.MaidenName);
            var targetNorm = target.NormalizedLastName ?? NormalizeForComparison(targetName);
            if (normalizedMaiden == targetNorm)
                return 0.95;
        }
        if (!string.IsNullOrEmpty(target.MaidenName))
        {
            var normalizedMaiden = NormalizeForComparison(target.MaidenName);
            var sourceNorm = source.NormalizedLastName ?? NormalizeForComparison(sourceName);
            if (normalizedMaiden == sourceNorm)
                return 0.95;
        }

        // 3. Check surname variants dictionary
        if (_nameVariants.AreEquivalentSurnames(sourceName, targetName))
            return 0.90;

        // Use pre-normalized names if available, otherwise normalize on-the-fly
        var sourceNormalized = source.NormalizedLastName ?? NormalizeForComparison(sourceName);
        var targetNormalized = target.NormalizedLastName ?? NormalizeForComparison(targetName);

        // 4. Jaro-Winkler similarity (better for transliteration and declensions in Slavic surnames)
        var similarity = _jaroWinkler.Similarity(sourceNormalized, targetNormalized);

        return similarity;
    }

    private IEnumerable<string> GetAllNameVariants(PersonRecord person, bool firstName)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (firstName)
        {
            if (!string.IsNullOrEmpty(person.FirstName))
                variants.Add(person.FirstName);
            if (!string.IsNullOrEmpty(person.Nickname))
                variants.Add(person.Nickname);
            if (!string.IsNullOrEmpty(person.MiddleName))
                variants.Add(person.MiddleName);
        }
        else
        {
            if (!string.IsNullOrEmpty(person.LastName))
                variants.Add(person.LastName);
            if (!string.IsNullOrEmpty(person.MaidenName))
                variants.Add(person.MaidenName);
        }

        // Add variants from GEDCOM name variants
        foreach (var v in person.NameVariants)
        {
            variants.Add(v);
        }

        return variants;
    }

    #endregion

    #region Date Comparison

    private double CompareDates(DateInfo? source, DateInfo? target)
    {
        if (source?.Date == null || target?.Date == null)
            return 0;

        var yearDiff = Math.Abs(source.Date.Value.Year - target.Date.Value.Year);

        // Exact year match
        if (yearDiff == 0)
        {
            // Compare based on precision
            var minPrecision = (DatePrecision)Math.Min((int)source.Precision, (int)target.Precision);

            if (minPrecision >= DatePrecision.Month)
            {
                if (source.Date.Value.Month == target.Date.Value.Month)
                {
                    if (minPrecision >= DatePrecision.Day)
                    {
                        return source.Date.Value.Day == target.Date.Value.Day ? 1.0 : 0.95;
                    }
                    return 0.95;
                }
                return 0.85;
            }
            return 0.90;
        }

        // Within tolerance
        if (yearDiff <= 1) return 0.80;
        if (yearDiff <= 2) return 0.60;
        if (yearDiff <= 5) return 0.40;
        if (yearDiff <= 10) return 0.20;

        return 0;
    }

    #endregion

    #region Place Comparison

    private double ComparePlaces(string? source, string? target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0;

        var sourceNorm = NormalizePlaceName(source);
        var targetNorm = NormalizePlaceName(target);

        // Exact match
        if (sourceNorm == targetNorm)
            return 1.0;

        // Check if one contains the other (city vs full address)
        if (sourceNorm.Contains(targetNorm) || targetNorm.Contains(sourceNorm))
            return 0.80;

        // Token matching (compare individual parts)
        var sourceTokens = sourceNorm.Split(',', ' ')
            .Select(t => t.Trim())
            .Where(t => t.Length > 2)
            .ToHashSet();
        
        var targetTokens = targetNorm.Split(',', ' ')
            .Select(t => t.Trim())
            .Where(t => t.Length > 2)
            .ToHashSet();

        if (sourceTokens.Count == 0 || targetTokens.Count == 0)
            return 0;

        var intersection = sourceTokens.Intersect(targetTokens).Count();
        var union = sourceTokens.Union(targetTokens).Count();

        // Jaccard similarity
        return (double)intersection / union;
    }

    private static string NormalizePlaceName(string place)
    {
        return place
            .ToLowerInvariant()
            .Replace(".", "")
            .Replace("-", " ")
            .Trim();
    }

    #endregion

    #region Gender Comparison

    private static double CompareGender(Gender source, Gender target)
    {
        if (source == Gender.Unknown || target == Gender.Unknown)
            return 1.0; // No penalty if unknown

        return source == target ? 1.0 : 0.0;
    }

    #endregion

    #region Normalization

    private string NormalizeForComparison(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Transliterate to common alphabet
        var transliterated = _nameVariants.Transliterate(text);

        return transliterated
            .ToLowerInvariant()
            .Replace("-", "")
            .Replace("'", "")
            .Replace(".", "")
            .Trim();
    }

    #endregion
}

/// <summary>
/// Options for matching algorithm
/// Immutable record for thread-safety
/// </summary>
public record MatchingOptions
{
    // Weights (should sum to ~100 for intuitive percentage)
    public int FirstNameWeight { get; init; } = 30;
    public int LastNameWeight { get; init; } = 25;
    public int BirthDateWeight { get; init; } = 20;
    public int BirthPlaceWeight { get; init; } = 15;
    public int DeathDateWeight { get; init; } = 5;
    public int GenderWeight { get; init; } = 5;

    // Thresholds
    public int MatchThreshold { get; init; } = 70;
    public int AutoMatchThreshold { get; init; } = 90;
    public int MaxBirthYearDifference { get; init; } = 10;

    /// <summary>
    /// Total weight (sum of all weights)
    /// </summary>
    public int TotalWeight => FirstNameWeight + LastNameWeight + BirthDateWeight +
                              BirthPlaceWeight + DeathDateWeight + GenderWeight;

    /// <summary>
    /// Check if weights are normalized (sum to 100)
    /// </summary>
    public bool AreWeightsNormalized => TotalWeight == 100;

    /// <summary>
    /// Get weight normalization factor to scale to 100
    /// </summary>
    public double NormalizationFactor => TotalWeight > 0 ? 100.0 / TotalWeight : 1.0;
}

/// <summary>
/// Service for name variants lookup and transliteration
/// </summary>
public class NameVariantsService
{
    private readonly Dictionary<string, HashSet<string>> _givenNameGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _surnameGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<NameVariantsService> _logger;

    // Cyrillic to Latin transliteration map
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // Ukrainian specific
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g",
        // Upper case
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D",
        ['Е'] = "E", ['Ё'] = "Yo", ['Ж'] = "Zh", ['З'] = "Z", ['И'] = "I",
        ['Й'] = "Y", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N",
        ['О'] = "O", ['П'] = "P", ['Р'] = "R", ['С'] = "S", ['Т'] = "T",
        ['У'] = "U", ['Ф'] = "F", ['Х'] = "Kh", ['Ц'] = "Ts", ['Ч'] = "Ch",
        ['Ш'] = "Sh", ['Щ'] = "Shch", ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "",
        ['Э'] = "E", ['Ю'] = "Yu", ['Я'] = "Ya",
        ['І'] = "I", ['Ї'] = "Yi", ['Є'] = "Ye", ['Ґ'] = "G"
    };

    public NameVariantsService(ILogger<NameVariantsService> logger)
    {
        _logger = logger;
        LoadBuiltInVariants();
    }

    /// <summary>
    /// Load CSV files with name variants
    /// </summary>
    public void LoadFromCsv(string givenNamesPath, string surnamesPath)
    {
        if (File.Exists(givenNamesPath))
        {
            LoadGivenNamesCsv(givenNamesPath);
        }

        if (File.Exists(surnamesPath))
        {
            LoadSurnamesCsv(surnamesPath);
        }
    }

    private void LoadGivenNamesCsv(string path)
    {
        _logger.LogInformation("Loading given names from {Path}", path);
        
        var lines = File.ReadAllLines(path);
        var count = 0;

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim().Trim('"');
                var variants = parts[1].Trim().Trim('"')
                    .Split('|')
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();

                AddGivenNameVariants(name, variants);
                count++;
            }
        }

        _logger.LogInformation("Loaded {Count} given name entries", count);
    }

    private void LoadSurnamesCsv(string path)
    {
        _logger.LogInformation("Loading surnames from {Path}", path);
        
        var lines = File.ReadAllLines(path);
        var count = 0;

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim().Trim('"');
                var variants = parts[1].Trim().Trim('"')
                    .Split('|')
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();

                AddSurnameVariants(name, variants);
                count++;
            }
        }

        _logger.LogInformation("Loaded {Count} surname entries", count);
    }

    /// <summary>
    /// Check if two given names are equivalent
    /// </summary>
    public bool AreEquivalent(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return false;

        var norm1 = name1.ToLowerInvariant().Trim();
        var norm2 = name2.ToLowerInvariant().Trim();

        if (norm1 == norm2)
            return true;

        // Check if in same group
        if (_givenNameGroups.TryGetValue(norm1, out var group1))
        {
            if (group1.Contains(norm2))
                return true;
        }

        if (_givenNameGroups.TryGetValue(norm2, out var group2))
        {
            if (group2.Contains(norm1))
                return true;
        }

        // Check transliterated versions
        var translit1 = Transliterate(norm1);
        var translit2 = Transliterate(norm2);

        if (translit1 == translit2)
            return true;

        if (_givenNameGroups.TryGetValue(translit1, out var group3))
        {
            if (group3.Contains(translit2))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if two surnames are equivalent
    /// </summary>
    public bool AreEquivalentSurnames(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return false;

        var norm1 = name1.ToLowerInvariant().Trim();
        var norm2 = name2.ToLowerInvariant().Trim();

        if (norm1 == norm2)
            return true;

        // Check if in same group
        if (_surnameGroups.TryGetValue(norm1, out var group1))
        {
            if (group1.Contains(norm2))
                return true;
        }

        if (_surnameGroups.TryGetValue(norm2, out var group2))
        {
            if (group2.Contains(norm1))
                return true;
        }

        // Check transliterated versions
        var translit1 = Transliterate(norm1);
        var translit2 = Transliterate(norm2);

        return translit1 == translit2;
    }

    /// <summary>
    /// Transliterate text from Cyrillic to Latin
    /// </summary>
    public string Transliterate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            if (CyrillicToLatin.TryGetValue(c, out var replacement))
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Add custom given name variants
    /// </summary>
    public void AddGivenNameVariants(string baseName, IEnumerable<string> variants)
    {
        var key = baseName.ToLowerInvariant();
        
        if (!_givenNameGroups.ContainsKey(key))
        {
            _givenNameGroups[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var variant in variants)
        {
            var variantKey = variant.ToLowerInvariant();
            _givenNameGroups[key].Add(variantKey);
            
            // Also add reverse mapping
            if (!_givenNameGroups.ContainsKey(variantKey))
            {
                _givenNameGroups[variantKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            _givenNameGroups[variantKey].Add(key);
        }
    }

    /// <summary>
    /// Add custom surname variants
    /// </summary>
    public void AddSurnameVariants(string baseName, IEnumerable<string> variants)
    {
        var key = baseName.ToLowerInvariant();
        
        if (!_surnameGroups.ContainsKey(key))
        {
            _surnameGroups[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var variant in variants)
        {
            var variantKey = variant.ToLowerInvariant();
            _surnameGroups[key].Add(variantKey);
            
            // Also add reverse mapping
            if (!_surnameGroups.ContainsKey(variantKey))
            {
                _surnameGroups[variantKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            _surnameGroups[variantKey].Add(key);
        }
    }

    /// <summary>
    /// Load built-in common variants (Slavic names focus)
    /// </summary>
    private void LoadBuiltInVariants()
    {
        // Common Russian/Ukrainian/Polish given name equivalents
        var givenNameEquivalents = new Dictionary<string, string[]>
        {
            // Male names
            ["иван"] = new[] { "ivan", "john", "johann", "jan", "jean", "giovanni", "juan", "ioan" },
            ["александр"] = new[] { "alexander", "alex", "oleksandr", "aleksander", "саша", "sasha" },
            ["михаил"] = new[] { "michael", "michel", "miguel", "mykhailo", "michal", "миша" },
            ["николай"] = new[] { "nicholas", "nicolas", "mykola", "mikolaj", "коля" },
            ["пётр"] = new[] { "peter", "pierre", "pedro", "petro", "piotr", "петр" },
            ["павел"] = new[] { "paul", "pavel", "pawel", "pablo", "паша" },
            ["андрей"] = new[] { "andrew", "andrei", "andriy", "andrzej", "andre" },
            ["сергей"] = new[] { "sergei", "serge", "sergiy", "серёжа" },
            ["дмитрий"] = new[] { "dmitry", "dmitri", "dmytro", "дима" },
            ["владимир"] = new[] { "vladimir", "volodymyr", "wladimir", "володя" },
            ["борис"] = new[] { "boris", "borys" },
            ["григорий"] = new[] { "gregory", "grigory", "hryhoriy", "гриша" },
            ["василий"] = new[] { "vasily", "basil", "vasyl", "вася" },
            ["яков"] = new[] { "jacob", "james", "jakub", "yakov" },
            ["семён"] = new[] { "simon", "semen", "семен" },
            ["фёдор"] = new[] { "theodore", "fedor", "федор", "федя" },
            
            // Female names
            ["мария"] = new[] { "maria", "mary", "marie", "марія", "маша" },
            ["анна"] = new[] { "anna", "anne", "ann", "hanna", "ганна", "аня" },
            ["елена"] = new[] { "helen", "helena", "elena", "olena", "лена" },
            ["екатерина"] = new[] { "catherine", "katarina", "kateryna", "катя" },
            ["наталья"] = new[] { "natalia", "natalie", "nataliya", "наташа" },
            ["ольга"] = new[] { "olga", "olha", "helga" },
            ["татьяна"] = new[] { "tatiana", "tanya", "tetiana", "таня" },
            ["ирина"] = new[] { "irina", "irene", "iryna" },
            ["светлана"] = new[] { "svetlana", "svitlana", "света" },
            ["людмила"] = new[] { "ludmila", "lyudmila", "liudmyla", "люда" },
            ["евгения"] = new[] { "eugenia", "yevheniya", "женя" },
            ["софья"] = new[] { "sophia", "sofia", "zofia", "софія", "соня" },
            ["елизавета"] = new[] { "elizabeth", "yelyzaveta", "elzbieta", "лиза" },
            ["валентина"] = new[] { "valentina", "валя" },
            ["галина"] = new[] { "galina", "halyna", "галя" }
        };

        foreach (var (key, variants) in givenNameEquivalents)
        {
            AddGivenNameVariants(key, variants);
        }

        _logger.LogInformation("Loaded {Count} built-in given name groups", givenNameEquivalents.Count);
    }
}
