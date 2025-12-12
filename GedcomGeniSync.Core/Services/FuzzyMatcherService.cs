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

        // Last name comparison first to check if we have a good match via MaidenName
        var lastNameScore = CompareLastNames(source, target);

        // Debug log for problematic cases
        if (lastNameScore >= 0.95 && (string.IsNullOrEmpty(source.LastName) || string.IsNullOrEmpty(target.LastName)))
        {
            _logger.LogDebug("Strong LastName match via MaidenName! Source: FirstName='{SrcFirst}', LastName='{SrcLast}', MaidenName='{SrcMaiden}'; Target: FirstName='{TgtFirst}', LastName='{TgtLast}', MaidenName='{TgtMaiden}'",
                source.FirstName, source.LastName ?? "(null)", source.MaidenName ?? "(null)",
                target.FirstName, target.LastName ?? "(null)", target.MaidenName ?? "(null)");
        }

        // Check if either has missing last name to adjust weights
        // BUT: Don't reduce weight if we successfully matched via MaidenName (score >= 0.95)
        var missingLastName = string.IsNullOrEmpty(source.LastName) || string.IsNullOrEmpty(target.LastName);
        var hasStrongLastNameMatch = lastNameScore >= 0.95; // MaidenName match returns 1.0

        // First name comparison - give it more weight if last name is missing AND not matched via maiden name
        var firstNameWeight = (missingLastName && !hasStrongLastNameMatch)
            ? _options.FirstNameWeight + (_options.LastNameWeight / 2)
            : _options.FirstNameWeight;
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

        // Last name comparison - reduce weight ONLY if missing AND not matched via maiden name
        var lastNameWeight = (missingLastName && !hasStrongLastNameMatch)
            ? _options.LastNameWeight / 2
            : _options.LastNameWeight;
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

        // IMPORTANT: Special bonus for strong MaidenName match combined with good FirstName and BirthDate
        // This handles cases where:
        // - LastName is missing but matched via MaidenName (e.g., Geni males often have null LastName)
        // - FirstName is strong match but not perfect (e.g., "Владимир" vs "Владимир Витальевич")
        // - BirthDate matches
        // This combination gives high confidence even if individual scores aren't 100%
        if (lastNameScore >= 0.95 && firstNameScore >= 0.85 && birthDateScore >= 0.85)
        {
            var bonus = 15.0; // Add bonus to compensate for patronymic differences
            reasonsBuilder.Add(new MatchReason
            {
                Field = "MaidenNameComboBonus",
                Points = bonus,
                Details = "Strong combination: MaidenName match + FirstName + BirthDate"
            });
            _logger.LogDebug("Applied MaidenName combo bonus of {Bonus} points (LastName:{LS:P0}, FirstName:{FS:P0}, BirthDate:{BS:P0})",
                bonus, lastNameScore, firstNameScore, birthDateScore);
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

        // Family relations comparison
        var familyScore = CompareFamilyRelations(source, target);
        if (familyScore > 0)
        {
            reasonsBuilder.Add(new MatchReason
            {
                Field = "FamilyRelations",
                Points = familyScore * _options.FamilyRelationsWeight,
                Details = $"Common family members ({familyScore:P0})"
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

        // IMPORTANT: Check maiden name BEFORE early exit for missing LastName
        // This handles cases where LastName is null but MaidenName is present (common in Geni for males)

        // If source has no LastName but has MaidenName, compare it with target's LastName
        if (string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(source.MaidenName) && !string.IsNullOrEmpty(targetName))
        {
            var normalizedMaiden = NormalizeForComparison(source.MaidenName);
            var targetNorm = target.NormalizedLastName ?? NormalizeForComparison(targetName);
            if (normalizedMaiden == targetNorm)
                return 1.0; // Exact match via MaidenName
        }

        // If target has no LastName but has MaidenName, compare it with source's LastName
        if (string.IsNullOrEmpty(targetName) && !string.IsNullOrEmpty(target.MaidenName) && !string.IsNullOrEmpty(sourceName))
        {
            var normalizedMaiden = NormalizeForComparison(target.MaidenName);
            var sourceNorm = source.NormalizedLastName ?? NormalizeForComparison(sourceName);
            _logger.LogDebug("Comparing target MaidenName '{TargetMaidenName}' (normalized: '{NormMaiden}') with source LastName '{SourceLastName}' (normalized: '{NormSource}')",
                target.MaidenName, normalizedMaiden, sourceName, sourceNorm);
            if (normalizedMaiden == sourceNorm)
            {
                _logger.LogDebug("MATCH via MaidenName! Returning score 1.0");
                return 1.0; // Exact match via MaidenName
            }
        }

        // If one is empty but the other is not (and no MaidenName match above), we can't confirm a match
        // Return a neutral score instead of 0 to not penalize missing data
        if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(targetName))
            return 0.3; // Small positive score - missing data shouldn't be a strong negative

        // 1. Exact match using pre-normalized names (optimization)
        if (!string.IsNullOrEmpty(source.NormalizedLastName) &&
            !string.IsNullOrEmpty(target.NormalizedLastName) &&
            source.NormalizedLastName == target.NormalizedLastName)
            return 1.0;

        // 2. Check maiden name (when both have LastName, but might also have MaidenName)
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

    #region Family Relations Comparison

    private Dictionary<string, PersonRecord>? _sourcePersonsCache;
    private Dictionary<string, PersonRecord>? _destPersonsCache;

    /// <summary>
    /// Set person dictionaries for family relations comparison
    /// This allows comparing family members by name when IDs don't match
    /// </summary>
    public void SetPersonDictionaries(
        Dictionary<string, PersonRecord>? sourcePersons,
        Dictionary<string, PersonRecord>? destPersons)
    {
        _sourcePersonsCache = sourcePersons;
        _destPersonsCache = destPersons;
    }

    /// <summary>
    /// Compare family relations between two persons
    /// Returns a score between 0.0 and 1.0 based on matching family members
    /// Now uses name-based comparison when person dictionaries are available
    /// </summary>
    private double CompareFamilyRelations(PersonRecord source, PersonRecord target)
    {
        var matchPoints = 0.0;
        var totalPossiblePoints = 0.0;

        // Compare parents (highest weight - 40% of family score each)
        var parentWeight = 0.4;

        // Compare fathers
        if (!string.IsNullOrEmpty(source.FatherId) || !string.IsNullOrEmpty(target.FatherId))
        {
            totalPossiblePoints += parentWeight;
            var fatherMatch = CompareRelativePair(source.FatherId, target.FatherId);
            matchPoints += fatherMatch * parentWeight;
        }

        // Compare mothers
        if (!string.IsNullOrEmpty(source.MotherId) || !string.IsNullOrEmpty(target.MotherId))
        {
            totalPossiblePoints += parentWeight;
            var motherMatch = CompareRelativePair(source.MotherId, target.MotherId);
            matchPoints += motherMatch * parentWeight;
        }

        // Compare spouses (30% of family score)
        var spouseWeight = 0.3;
        if (source.SpouseIds.Any() || target.SpouseIds.Any())
        {
            totalPossiblePoints += spouseWeight;
            var spouseMatch = CompareRelativeList(source.SpouseIds, target.SpouseIds);
            matchPoints += spouseMatch * spouseWeight;
        }

        // Compare children (20% of family score)
        var childrenWeight = 0.2;
        if (source.ChildrenIds.Any() || target.ChildrenIds.Any())
        {
            totalPossiblePoints += childrenWeight;
            var childrenMatch = CompareRelativeList(source.ChildrenIds, target.ChildrenIds);
            matchPoints += childrenMatch * childrenWeight;
        }

        // Compare siblings (10% of family score)
        var siblingWeight = 0.1;
        if (source.SiblingIds.Any() || target.SiblingIds.Any())
        {
            totalPossiblePoints += siblingWeight;
            var siblingMatch = CompareRelativeList(source.SiblingIds, target.SiblingIds);
            matchPoints += siblingMatch * siblingWeight;
        }

        // If no comparable family data, return 0 (neutral, not penalty)
        if (totalPossiblePoints == 0)
            return 0.0;

        // Return normalized score
        return matchPoints / totalPossiblePoints;
    }

    /// <summary>
    /// Compare two relative IDs by resolving to PersonRecords and comparing names
    /// Returns 1.0 if they match, 0.0 if not
    /// </summary>
    private double CompareRelativePair(string? sourceId, string? targetId)
    {
        // If both are null/empty, no data to compare
        if (string.IsNullOrEmpty(sourceId) && string.IsNullOrEmpty(targetId))
            return 0.0;

        // If only one is null, it's a mismatch
        if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
            return 0.0;

        // Try to resolve persons from cache
        PersonRecord? sourcePerson = null;
        PersonRecord? targetPerson = null;

        if (_sourcePersonsCache != null)
            _sourcePersonsCache.TryGetValue(sourceId, out sourcePerson);

        if (_destPersonsCache != null)
            _destPersonsCache.TryGetValue(targetId, out targetPerson);

        // If we couldn't resolve both persons, fall back to ID comparison
        if (sourcePerson == null || targetPerson == null)
        {
            return sourceId == targetId ? 1.0 : 0.0;
        }

        // Compare by names
        return CompareRelativesByName(sourcePerson, targetPerson);
    }

    /// <summary>
    /// Compare two lists of relative IDs by finding best matches
    /// Returns a score from 0.0 to 1.0 based on how many match
    /// </summary>
    private double CompareRelativeList(ImmutableList<string> sourceIds, ImmutableList<string> targetIds)
    {
        if (!sourceIds.Any() && !targetIds.Any())
            return 0.0;

        if (!sourceIds.Any() || !targetIds.Any())
            return 0.0;

        // Try to resolve all persons
        var sourcePersons = sourceIds
            .Select(id => _sourcePersonsCache?.GetValueOrDefault(id))
            .Where(p => p != null)
            .ToList()!;

        var targetPersons = targetIds
            .Select(id => _destPersonsCache?.GetValueOrDefault(id))
            .Where(p => p != null)
            .ToList()!;

        // If we couldn't resolve any persons, fall back to ID intersection
        if (!sourcePersons.Any() || !targetPersons.Any())
        {
            var commonIds = sourceIds.Intersect(targetIds).Count();
            var totalIds = sourceIds.Union(targetIds).Count();
            return totalIds > 0 ? (double)commonIds / totalIds : 0.0;
        }

        // Find matching pairs by name
        var matchedCount = 0;
        var matched = new HashSet<PersonRecord>();

        foreach (var sourcePerson in sourcePersons)
        {
            foreach (var targetPerson in targetPersons)
            {
                if (matched.Contains(targetPerson))
                    continue;

                // Check if names match well
                if (CompareRelativesByName(sourcePerson, targetPerson) >= 0.8)
                {
                    matchedCount++;
                    matched.Add(targetPerson);
                    break; // Move to next source person
                }
            }
        }

        // Jaccard-like similarity: matched / total unique
        var totalUnique = Math.Max(sourcePersons.Count, targetPersons.Count);
        return totalUnique > 0 ? (double)matchedCount / totalUnique : 0.0;
    }

    /// <summary>
    /// Compare two persons by name to determine if they are likely the same person
    /// Used for family relations matching
    /// Returns 1.0 for strong match, 0.0 for no match
    /// </summary>
    private double CompareRelativesByName(PersonRecord person1, PersonRecord person2)
    {
        // Compare first names
        var firstName1 = person1.NormalizedFirstName ?? NormalizeForComparison(person1.FirstName ?? "");
        var firstName2 = person2.NormalizedFirstName ?? NormalizeForComparison(person2.FirstName ?? "");

        if (string.IsNullOrEmpty(firstName1) || string.IsNullOrEmpty(firstName2))
            return 0.0;

        var firstNameSimilarity = _jaroWinkler.Similarity(firstName1, firstName2);
        if (firstNameSimilarity < 0.8)
            return 0.0; // First name must match well

        // Compare last names (or maiden names)
        var lastName1 = person1.NormalizedLastName ?? NormalizeForComparison(person1.LastName ?? person1.MaidenName ?? "");
        var lastName2 = person2.NormalizedLastName ?? NormalizeForComparison(person2.LastName ?? person2.MaidenName ?? "");

        if (string.IsNullOrEmpty(lastName1) || string.IsNullOrEmpty(lastName2))
        {
            // If last name missing for both but first name matches well, consider it a match
            return firstNameSimilarity >= 0.9 ? 1.0 : 0.5;
        }

        var lastNameSimilarity = _jaroWinkler.Similarity(lastName1, lastName2);

        // Both names need to match reasonably well
        var avgSimilarity = (firstNameSimilarity + lastNameSimilarity) / 2.0;

        // Return 1.0 if average is high, or scaled value if moderate
        return avgSimilarity >= 0.85 ? 1.0 : (avgSimilarity >= 0.7 ? 0.5 : 0.0);
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
    public int FirstNameWeight { get; init; } = 25;
    public int LastNameWeight { get; init; } = 20;
    public int BirthDateWeight { get; init; } = 15;
    public int BirthPlaceWeight { get; init; } = 10;
    public int DeathDateWeight { get; init; } = 3;
    public int GenderWeight { get; init; } = 2;
    public int FamilyRelationsWeight { get; init; } = 25;

    // Thresholds
    public int MatchThreshold { get; init; } = 70;
    public int AutoMatchThreshold { get; init; } = 90;
    public int MaxBirthYearDifference { get; init; } = 10;

    /// <summary>
    /// Total weight (sum of all weights)
    /// </summary>
    public int TotalWeight => FirstNameWeight + LastNameWeight + BirthDateWeight +
                              BirthPlaceWeight + DeathDateWeight + GenderWeight + FamilyRelationsWeight;

    /// <summary>
    /// Check if weights are normalized (sum to 100)
    /// </summary>
    public bool AreWeightsNormalized => TotalWeight == 100;

    /// <summary>
    /// Get weight normalization factor to scale to 100
    /// </summary>
    public double NormalizationFactor => TotalWeight > 0 ? 100.0 / TotalWeight : 1.0;
}
