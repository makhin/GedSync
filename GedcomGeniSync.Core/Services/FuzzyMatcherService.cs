using System.Collections.Immutable;
using F23.StringSimilarity;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Fuzzy matching service for comparing PersonRecords
/// Uses Jaro-Winkler algorithm optimized for Slavic languages
/// </summary>
public class FuzzyMatcherService : IFuzzyMatcherService
{    
    private readonly INameVariantsService _nameVariants;
    private readonly ILogger<FuzzyMatcherService> _logger;
    private readonly MatchingOptions _options;
    private readonly JaroWinkler _jaroWinkler;

    public FuzzyMatcherService(
        INameVariantsService nameVariants,
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

        // Check if either has missing last name to adjust weights
        var missingLastName = string.IsNullOrEmpty(source.LastName) || string.IsNullOrEmpty(target.LastName);

        // First name comparison - give it more weight if last name is missing
        var firstNameWeight = missingLastName ? _options.FirstNameWeight + (_options.LastNameWeight / 2) : _options.FirstNameWeight;
        var firstNameScore = CompareFirstNames(source, target);
        if (firstNameScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "FirstName",
                Points = firstNameScore * firstNameWeight,
                Details = $"{source.FirstName} ↔ {target.FirstName} ({firstNameScore:P0})"
            });
        }

        // Last name comparison - reduce weight if missing
        var lastNameWeight = missingLastName ? _options.LastNameWeight / 2 : _options.LastNameWeight;
        var lastNameScore = CompareLastNames(source, target);
        if (lastNameScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "LastName",
                Points = lastNameScore * lastNameWeight,
                Details = $"{source.LastName} ↔ {target.LastName} ({lastNameScore:P0})"
            });
        }

        // Maiden name comparison - higher weight than last name as it's more stable
        // Maiden name is birth surname and doesn't change with marriage
        var maidenNameScore = CompareMaidenNames(source, target);
        if (maidenNameScore > 0)
        {
            // Give maiden name slightly higher weight than last name (30% more)
            var maidenNameWeight = lastNameWeight * 1.3;
            reasonsBuilder.Add(new MatchReason
            {
                Field = "MaidenName",
                Points = maidenNameScore * maidenNameWeight,
                Details = $"{source.MaidenName} ↔ {target.MaidenName} ({maidenNameScore:P0})"
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
        var allMatches = new List<MatchCandidate>(); // Track all for debugging

        foreach (var candidate in candidates)
        {
            // Quick pre-filter: skip if gender definitely mismatches
            if (source.Gender != Gender.Unknown &&
                candidate.Gender != Gender.Unknown &&
                source.Gender != candidate.Gender)
            {
                _logger.LogTrace("Skipping candidate {Name} - gender mismatch ({Source} != {Target})",
                    candidate.FullName, source.Gender, candidate.Gender);
                continue;
            }

            // Quick pre-filter: skip if birth years are too far apart
            if (source.BirthYear.HasValue && candidate.BirthYear.HasValue)
            {
                var yearDiff = Math.Abs(source.BirthYear.Value - candidate.BirthYear.Value);
                if (yearDiff > _options.MaxBirthYearDifference)
                {
                    _logger.LogTrace("Skipping candidate {Name} - birth year too different ({SourceYear} vs {TargetYear}, diff: {Diff})",
                        candidate.FullName, source.BirthYear, candidate.BirthYear, yearDiff);
                    continue;
                }
            }

            var match = Compare(source, candidate);
            allMatches.Add(match);

            // Log ALL candidates with their scores for debugging
            var reasons = string.Join(", ", match.Reasons.Select(r => $"{r.Field}:{r.Points:F1}"));
            _logger.LogDebug("Candidate match: {SourceName} vs {TargetName} - Score: {Score:F1}% ({Reasons})",
                source.FullName, candidate.FullName, match.Score, reasons);

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

        // 4. Check if one name is the first word of the other (e.g., "Владимир" vs "Владимир Витальевич")
        // This is common when one source has patronymic included and the other doesn't
        var sourceWords = sourceNorm.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var targetWords = targetNorm.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

        // If first words match exactly, this is likely the same person
        if (sourceWords.Length > 0 && targetWords.Length > 0 &&
            sourceWords[0] == targetWords[0])
        {
            // Full match if both have just one word
            if (sourceWords.Length == 1 && targetWords.Length == 1)
                return 1.0;

            // Strong match if one has patronymic/middle name and the other doesn't
            // e.g., "Владимир" (1 word) vs "Владимир Витальевич" (2 words)
            if (Math.Abs(sourceWords.Length - targetWords.Length) == 1)
                return 0.90; // High confidence - first name matches, just missing patronymic

            // Still good match if both have multiple words but first matches
            return 0.85;
        }

        // 5. Jaro-Winkler similarity (better for transliteration and Slavic languages)
        var similarity = _jaroWinkler.Similarity(sourceNorm, targetNorm);

        // 6. Check if one name is a substring of the other (for nicknames like Александр vs Саша)
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

        // If both are empty, consider it neutral (not a mismatch)
        if (string.IsNullOrEmpty(sourceName) && string.IsNullOrEmpty(targetName))
            return 0.5; // Neutral score instead of 0

        // If one is empty but the other is not, we can't confirm a match
        // Return a neutral score instead of 0 to not penalize missing data
        if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(targetName))
            return 0.3; // Small positive score - missing data shouldn't be a strong negative

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

    private double CompareMaidenNames(PersonRecord source, PersonRecord target)
    {
        var sourceMaiden = source.MaidenName;
        var targetMaiden = target.MaidenName;

        // If both are empty, return 0 (not applicable)
        if (string.IsNullOrEmpty(sourceMaiden) && string.IsNullOrEmpty(targetMaiden))
            return 0;

        // If one has maiden name and the other doesn't, we can't compare
        if (string.IsNullOrEmpty(sourceMaiden) || string.IsNullOrEmpty(targetMaiden))
            return 0;

        // Both have maiden names - compare them
        var sourceNorm = NormalizeForComparison(sourceMaiden);
        var targetNorm = NormalizeForComparison(targetMaiden);

        // 1. Exact match
        if (sourceNorm == targetNorm)
            return 1.0;

        // 2. Check if they're equivalent surnames in the dictionary
        if (_nameVariants.AreEquivalentSurnames(sourceMaiden, targetMaiden))
            return 0.95;

        // 3. Jaro-Winkler similarity
        var similarity = _jaroWinkler.Similarity(sourceNorm, targetNorm);

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
