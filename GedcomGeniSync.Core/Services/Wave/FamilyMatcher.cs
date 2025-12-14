using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Поиск соответствующей семьи в destination дереве на основе уже известных сопоставлений.
/// Использует структурный подход: если супруги сопоставлены, их семьи должны совпадать.
/// Также учитывает персональное сходство супругов при выборе лучшей семьи.
/// </summary>
public class FamilyMatcher
{
    private readonly ILogger<FamilyMatcher>? _logger;
    private readonly IFuzzyMatcherService? _fuzzyMatcher;

    public FamilyMatcher(ILogger<FamilyMatcher>? logger = null, IFuzzyMatcherService? fuzzyMatcher = null)
    {
        _logger = logger;
        _fuzzyMatcher = fuzzyMatcher;
    }

    /// <summary>
    /// Найти соответствующую семью в списке destination семей.
    /// Использует уже известные сопоставления персон для структурного поиска.
    /// </summary>
    /// <param name="sourceFamily">Семья из source дерева</param>
    /// <param name="destFamilies">Список кандидатов семей из destination дерева</param>
    /// <param name="mappings">Уже известные сопоставления персон (sourceId -> PersonMapping)</param>
    /// <returns>Лучшая подходящая семья или null если не найдено</returns>
    public FamilyRecord? FindMatchingFamily(
        FamilyRecord sourceFamily,
        IEnumerable<FamilyRecord> destFamilies,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        var (family, _) = FindMatchingFamilyWithLog(sourceFamily, destFamilies, mappings, null, null);
        return family;
    }

    /// <summary>
    /// Найти соответствующую семью с детальным логированием.
    /// </summary>
    public (FamilyRecord? family, FamilyMatchAttemptLog log) FindMatchingFamilyWithLog(
        FamilyRecord sourceFamily,
        IEnumerable<FamilyRecord> destFamilies,
        IReadOnlyDictionary<string, PersonMapping> mappings,
        TreeGraph? sourceTree,
        TreeGraph? destTree)
    {
        FamilyRecord? bestFamily = null;
        int bestScore = 0;
        var candidateLogs = new List<CandidateFamilyLog>();

        var destFamiliesList = destFamilies.ToList();

        foreach (var destFamily in destFamiliesList)
        {
            var (structureScore, hasConflict, scoreBreakdown, conflictReason) = CalculateFamilyMatchScoreDetailed(
                sourceFamily,
                destFamily,
                mappings);

            // Вычисляем комбинированный score с учетом персональных совпадений супругов
            int totalScore = CalculateCombinedScore(
                sourceFamily,
                destFamily,
                mappings,
                sourceTree,
                destTree,
                structureScore,
                scoreBreakdown);

            // Создаем лог для кандидата
            candidateLogs.Add(new CandidateFamilyLog
            {
                DestFamilyId = destFamily.Id,
                Structure = BuildFamilyStructureDescription(destFamily, destTree),
                StructureScore = totalScore, // Используем комбинированный score
                ScoreBreakdown = scoreBreakdown,
                HasConflict = hasConflict,
                ConflictReason = conflictReason
            });

            if (!hasConflict && totalScore > bestScore)
            {
                bestScore = totalScore;
                bestFamily = destFamily;
            }
        }

        // Определяем результат
        FamilyMatchResult matchResult;
        string? noMatchReason = null;

        if (destFamiliesList.Count == 0)
        {
            matchResult = FamilyMatchResult.NoCandidates;
            noMatchReason = "No destination families to match against";
        }
        else if (bestFamily != null)
        {
            matchResult = FamilyMatchResult.Matched;
            _logger?.LogDebug(
                "Matched family {SourceId} -> {DestId} with score {Score}",
                sourceFamily.Id, bestFamily.Id, bestScore);
        }
        else if (candidateLogs.Any(c => c.HasConflict))
        {
            matchResult = FamilyMatchResult.Conflict;
            noMatchReason = "All candidates have conflicts with existing mappings";
        }
        else
        {
            matchResult = FamilyMatchResult.NoMatch;
            noMatchReason = "No candidates with score > 0";
        }

        var log = new FamilyMatchAttemptLog
        {
            SourceFamilyId = sourceFamily.Id,
            SourceStructure = BuildFamilyStructureDescription(sourceFamily, sourceTree),
            Candidates = candidateLogs,
            MatchResult = matchResult,
            MatchedDestFamilyId = bestFamily?.Id,
            BestScore = bestScore > 0 ? bestScore : null,
            NoMatchReason = noMatchReason
        };

        return (bestFamily, log);
    }

    private FamilyStructureDescription BuildFamilyStructureDescription(
        FamilyRecord family,
        TreeGraph? tree)
    {
        string? husbandName = null;
        string? wifeName = null;
        var childNames = new List<string>();

        if (tree != null)
        {
            if (family.HusbandId != null && tree.PersonsById.TryGetValue(family.HusbandId, out var husband))
                husbandName = husband.ToString();

            if (family.WifeId != null && tree.PersonsById.TryGetValue(family.WifeId, out var wife))
                wifeName = wife.ToString();

            foreach (var childId in family.ChildIds)
            {
                if (tree.PersonsById.TryGetValue(childId, out var child))
                    childNames.Add(child.ToString());
            }
        }

        return new FamilyStructureDescription
        {
            HusbandId = family.HusbandId,
            HusbandName = husbandName,
            WifeId = family.WifeId,
            WifeName = wifeName,
            ChildIds = family.ChildIds.ToList(),
            ChildNames = childNames
        };
    }

    /// <summary>
    /// Вычислить комбинированный score с учетом структурного сходства и персональных совпадений супругов.
    /// </summary>
    private int CalculateCombinedScore(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings,
        TreeGraph? sourceTree,
        TreeGraph? destTree,
        int structureScore,
        List<ScoreComponent> scoreBreakdown)
    {
        // Если нет FuzzyMatcher или деревьев, используем только структурный score
        if (_fuzzyMatcher == null || sourceTree == null || destTree == null)
            return structureScore;

        int husbandScore = 0;
        int wifeScore = 0;
        bool hasHusbandToMatch = false;
        bool hasWifeToMatch = false;

        // Вычисляем персональный score для мужа (если еще не сопоставлен)
        if (sourceFamily.HusbandId != null &&
            destFamily.HusbandId != null &&
            !mappings.ContainsKey(sourceFamily.HusbandId))
        {
            hasHusbandToMatch = true;
            if (sourceTree.PersonsById.TryGetValue(sourceFamily.HusbandId, out var sourceHusband) &&
                destTree.PersonsById.TryGetValue(destFamily.HusbandId, out var destHusband))
            {
                var result = _fuzzyMatcher.Compare(sourceHusband, destHusband);
                husbandScore = (int)result.Score;

                // Добавляем в breakdown
                scoreBreakdown.Add(new ScoreComponent
                {
                    Component = "Husband Personal Score",
                    Points = husbandScore,
                    Description = $"Personal similarity: {husbandScore}%"
                });
            }
        }

        // Вычисляем персональный score для жены (если еще не сопоставлена)
        if (sourceFamily.WifeId != null &&
            destFamily.WifeId != null &&
            !mappings.ContainsKey(sourceFamily.WifeId))
        {
            hasWifeToMatch = true;
            if (sourceTree.PersonsById.TryGetValue(sourceFamily.WifeId, out var sourceWife) &&
                destTree.PersonsById.TryGetValue(destFamily.WifeId, out var destWife))
            {
                var result = _fuzzyMatcher.Compare(sourceWife, destWife);
                wifeScore = (int)result.Score;

                // Добавляем в breakdown
                scoreBreakdown.Add(new ScoreComponent
                {
                    Component = "Wife Personal Score",
                    Points = wifeScore,
                    Description = $"Personal similarity: {wifeScore}%"
                });
            }
        }

        // Вычисляем комбинированный score
        // Если есть супруги для сопоставления - учитываем их персональные scores
        if (hasHusbandToMatch || hasWifeToMatch)
        {
            // Веса: 40% структура, 30% муж, 30% жена
            // Если только один супруг - ему 60%
            double structureWeight = 0.4;
            double spouseWeight;

            if (hasHusbandToMatch && hasWifeToMatch)
            {
                spouseWeight = 0.3; // Каждому по 30%
                int combinedScore = (int)(
                    structureScore * structureWeight +
                    husbandScore * spouseWeight +
                    wifeScore * spouseWeight);

                scoreBreakdown.Add(new ScoreComponent
                {
                    Component = "Combined Total",
                    Points = combinedScore,
                    Description = $"Structure: {structureScore}*0.4 + Husband: {husbandScore}*0.3 + Wife: {wifeScore}*0.3"
                });

                return combinedScore;
            }
            else if (hasHusbandToMatch)
            {
                spouseWeight = 0.6; // 60% для единственного супруга
                int combinedScore = (int)(
                    structureScore * structureWeight +
                    husbandScore * spouseWeight);

                scoreBreakdown.Add(new ScoreComponent
                {
                    Component = "Combined Total",
                    Points = combinedScore,
                    Description = $"Structure: {structureScore}*0.4 + Husband: {husbandScore}*0.6"
                });

                return combinedScore;
            }
            else // hasWifeToMatch
            {
                spouseWeight = 0.6; // 60% для единственного супруга
                int combinedScore = (int)(
                    structureScore * structureWeight +
                    wifeScore * spouseWeight);

                scoreBreakdown.Add(new ScoreComponent
                {
                    Component = "Combined Total",
                    Points = combinedScore,
                    Description = $"Structure: {structureScore}*0.4 + Wife: {wifeScore}*0.6"
                });

                return combinedScore;
            }
        }

        // Если нет супругов для сопоставления - используем только структурный score
        return structureScore;
    }

    /// <summary>
    /// Вычислить score соответствия между двумя семьями с детализацией.
    /// </summary>
    private (int score, bool hasConflict, List<ScoreComponent> breakdown, string? conflictReason) CalculateFamilyMatchScoreDetailed(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        int score = 0;
        bool hasConflict = false;
        string? conflictReason = null;
        var breakdown = new List<ScoreComponent>();

        // Проверяем мужа
        if (sourceFamily.HusbandId != null)
        {
            if (mappings.TryGetValue(sourceFamily.HusbandId, out var husbandMapping))
            {
                if (husbandMapping.DestinationId == destFamily.HusbandId)
                {
                    score += 50;
                    breakdown.Add(new ScoreComponent
                    {
                        Component = "Husband Match",
                        Points = 50,
                        Description = $"Husband {sourceFamily.HusbandId} already mapped to {destFamily.HusbandId}"
                    });
                }
                else if (destFamily.HusbandId != null)
                {
                    hasConflict = true;
                    conflictReason = $"Husband {sourceFamily.HusbandId} mapped to {husbandMapping.DestinationId} but family has {destFamily.HusbandId}";
                }
            }
            else if (destFamily.HusbandId != null)
            {
                score += 10;
                breakdown.Add(new ScoreComponent
                {
                    Component = "Husband Present",
                    Points = 10,
                    Description = "Both families have husband (not yet mapped)"
                });
            }
        }

        // Проверяем жену
        if (sourceFamily.WifeId != null)
        {
            if (mappings.TryGetValue(sourceFamily.WifeId, out var wifeMapping))
            {
                if (wifeMapping.DestinationId == destFamily.WifeId)
                {
                    score += 50;
                    breakdown.Add(new ScoreComponent
                    {
                        Component = "Wife Match",
                        Points = 50,
                        Description = $"Wife {sourceFamily.WifeId} already mapped to {destFamily.WifeId}"
                    });
                }
                else if (destFamily.WifeId != null)
                {
                    hasConflict = true;
                    conflictReason = (conflictReason ?? "") + $" Wife {sourceFamily.WifeId} mapped to {wifeMapping.DestinationId} but family has {destFamily.WifeId}";
                }
            }
            else if (destFamily.WifeId != null)
            {
                score += 10;
                breakdown.Add(new ScoreComponent
                {
                    Component = "Wife Present",
                    Points = 10,
                    Description = "Both families have wife (not yet mapped)"
                });
            }
        }

        // Проверяем детей
        int matchedChildrenCount = 0;
        foreach (var childId in sourceFamily.ChildIds)
        {
            if (mappings.TryGetValue(childId, out var childMapping))
            {
                if (destFamily.ChildIds.Contains(childMapping.DestinationId))
                {
                    matchedChildrenCount++;
                }
                else
                {
                    hasConflict = true;
                    conflictReason = (conflictReason ?? "") + $" Child {childId} mapped to {childMapping.DestinationId} not in dest family";
                }
            }
        }

        if (matchedChildrenCount > 0)
        {
            int childPoints = matchedChildrenCount * 20;
            score += childPoints;
            breakdown.Add(new ScoreComponent
            {
                Component = "Children Match",
                Points = childPoints,
                Description = $"{matchedChildrenCount} children already mapped to this family"
            });
        }

        return (score, hasConflict, breakdown, conflictReason);
    }

    /// <summary>
    /// Вычислить score соответствия между двумя семьями.
    /// </summary>
    /// <returns>Tuple (score, hasConflict) где hasConflict указывает на несовместимость</returns>
    private (int score, bool hasConflict) CalculateFamilyMatchScore(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        var (score, hasConflict, _, _) = CalculateFamilyMatchScoreDetailed(sourceFamily, destFamily, mappings);
        return (score, hasConflict);
    }

    [Obsolete("Use CalculateFamilyMatchScoreDetailed instead")]
    private (int score, bool hasConflict) CalculateFamilyMatchScoreOld(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        int score = 0;
        bool hasConflict = false;

        // ═══════════════════════════════════════════════════════════
        // Проверяем мужа
        // ═══════════════════════════════════════════════════════════
        if (sourceFamily.HusbandId != null)
        {
            if (mappings.TryGetValue(sourceFamily.HusbandId, out var husbandMapping))
            {
                // Муж уже сопоставлен - проверяем совпадение
                if (husbandMapping.DestinationId == destFamily.HusbandId)
                {
                    score += 50;  // Муж совпадает — большой плюс
                }
                else if (destFamily.HusbandId != null)
                {
                    hasConflict = true;  // Муж сопоставлен с другим — конфликт
                }
            }
            else if (destFamily.HusbandId != null)
            {
                score += 10;  // Оба имеют мужа, но ещё не сопоставлен
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Проверяем жену
        // ═══════════════════════════════════════════════════════════
        if (sourceFamily.WifeId != null)
        {
            if (mappings.TryGetValue(sourceFamily.WifeId, out var wifeMapping))
            {
                // Жена уже сопоставлена - проверяем совпадение
                if (wifeMapping.DestinationId == destFamily.WifeId)
                {
                    score += 50;  // Жена совпадает — большой плюс
                }
                else if (destFamily.WifeId != null)
                {
                    hasConflict = true;  // Жена сопоставлена с другой — конфликт
                }
            }
            else if (destFamily.WifeId != null)
            {
                score += 10;  // Обе имеют жену, но ещё не сопоставлена
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Проверяем детей
        // ═══════════════════════════════════════════════════════════
        int matchedChildrenCount = 0;
        foreach (var childId in sourceFamily.ChildIds)
        {
            if (mappings.TryGetValue(childId, out var childMapping))
            {
                if (destFamily.ChildIds.Contains(childMapping.DestinationId))
                {
                    matchedChildrenCount++;
                }
                else
                {
                    // Ребёнок сопоставлен с кем-то из другой семьи — конфликт
                    hasConflict = true;
                }
            }
        }

        score += matchedChildrenCount * 20;  // Каждый совпавший ребёнок добавляет 20 очков

        return (score, hasConflict);
    }

    /// <summary>
    /// Проверить, является ли семья уже полностью сопоставленной.
    /// </summary>
    public bool IsFamilyFullyMapped(
        FamilyRecord sourceFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        // Проверяем супругов
        if (sourceFamily.HusbandId != null && !mappings.ContainsKey(sourceFamily.HusbandId))
            return false;

        if (sourceFamily.WifeId != null && !mappings.ContainsKey(sourceFamily.WifeId))
            return false;

        // Проверяем всех детей
        foreach (var childId in sourceFamily.ChildIds)
        {
            if (!mappings.ContainsKey(childId))
                return false;
        }

        return true;
    }
}
