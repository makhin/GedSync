using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;
using RelationType = GedcomGeniSync.Core.Models.Wave.RelationType;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Сопоставление членов одной семьи между source и destination деревьями.
/// Использует контекст семьи для более точного matching с низкими порогами.
/// </summary>
public class FamilyMemberMatcher
{
    private readonly IFuzzyMatcherService _fuzzyMatcher;
    private readonly ThresholdCalculator _thresholdCalculator;
    private readonly ILogger<FamilyMemberMatcher>? _logger;

    public FamilyMemberMatcher(
        IFuzzyMatcherService fuzzyMatcher,
        ThresholdCalculator thresholdCalculator,
        ILogger<FamilyMemberMatcher>? logger = null)
    {
        _fuzzyMatcher = fuzzyMatcher;
        _thresholdCalculator = thresholdCalculator;
        _logger = logger;
    }

    /// <summary>
    /// Сопоставить членов семьи (супругов и детей).
    /// </summary>
    public List<PersonMapping> MatchMembers(
        FamilyMatchContext context,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var newMappings = new List<PersonMapping>();
        var sourceFamily = context.SourceFamily;
        var destFamily = context.DestinationFamily;

        if (destFamily == null)
        {
            _logger?.LogWarning("DestinationFamily is null in context for source family {FamilyId}", sourceFamily.Id);
            return newMappings;
        }

        var existingMappings = context.ExistingMappings;
        var nextLevel = context.CurrentLevel + 1;

        // ═══════════════════════════════════════════════════════════
        // 1. СОПОСТАВЛЕНИЕ СУПРУГОВ
        // ═══════════════════════════════════════════════════════════

        // Муж
        if (sourceFamily.HusbandId != null &&
            !existingMappings.ContainsKey(sourceFamily.HusbandId) &&
            destFamily.HusbandId != null)
        {
            var mapping = MatchSpouse(
                sourceFamily.HusbandId,
                destFamily.HusbandId,
                sourceTree,
                destTree,
                nextLevel,
                sourceFamily.Id,
                context.FromPersonId);

            if (mapping != null)
            {
                newMappings.Add(mapping);
            }
        }

        // Жена
        if (sourceFamily.WifeId != null &&
            !existingMappings.ContainsKey(sourceFamily.WifeId) &&
            destFamily.WifeId != null)
        {
            var mapping = MatchSpouse(
                sourceFamily.WifeId,
                destFamily.WifeId,
                sourceTree,
                destTree,
                nextLevel,
                sourceFamily.Id,
                context.FromPersonId);

            if (mapping != null)
            {
                newMappings.Add(mapping);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 2. СОПОСТАВЛЕНИЕ ДЕТЕЙ
        // ═══════════════════════════════════════════════════════════

        var unmatchedSourceChildren = sourceFamily.ChildIds
            .Where(id => !existingMappings.ContainsKey(id))
            .Select(id => sourceTree.PersonsById[id])
            .ToList();

        var unmatchedDestChildren = destFamily.ChildIds
            .Where(id => !existingMappings.Values.Any(m => m == id))
            .Select(id => destTree.PersonsById[id])
            .ToList();

        if (unmatchedSourceChildren.Count > 0 && unmatchedDestChildren.Count > 0)
        {
            var childMappings = MatchChildrenSet(
                unmatchedSourceChildren,
                unmatchedDestChildren,
                nextLevel,
                sourceFamily.Id,
                context.FromPersonId);

            newMappings.AddRange(childMappings);
        }

        return newMappings;
    }

    /// <summary>
    /// Сопоставить одного супруга.
    /// </summary>
    private PersonMapping? MatchSpouse(
        string sourceId,
        string destId,
        TreeGraph sourceTree,
        TreeGraph destTree,
        int level,
        string familyId,
        string fromPersonId)
    {
        var sourcePerson = sourceTree.PersonsById[sourceId];
        var destPerson = destTree.PersonsById[destId];

        var matchResult = _fuzzyMatcher.Compare(sourcePerson, destPerson);
        var threshold = _thresholdCalculator.GetSpouseThreshold();

        if (matchResult.Score >= threshold)
        {
            _logger?.LogDebug(
                "Matched spouse {SourceId} -> {DestId} with score {Score}",
                sourceId, destId, matchResult.Score);

            return new PersonMapping
            {
                SourceId = sourceId,
                DestinationId = destId,
                MatchScore = (int)matchResult.Score,
                Level = level,
                FoundVia = RelationType.Spouse,
                FoundInFamilyId = familyId,
                FoundFromPersonId = fromPersonId,
                FoundAt = DateTime.UtcNow
            };
        }

        _logger?.LogDebug(
            "Spouse {SourceId} -> {DestId} score {Score} below threshold {Threshold}",
            sourceId, destId, matchResult.Score, threshold);

        return null;
    }

    /// <summary>
    /// Сопоставить набор детей одной семьи.
    /// Использует жадный алгоритм с матрицей схожести.
    /// </summary>
    private List<PersonMapping> MatchChildrenSet(
        List<PersonRecord> sourceChildren,
        List<PersonRecord> destChildren,
        int level,
        string familyId,
        string fromPersonId)
    {
        int n = sourceChildren.Count;
        int m = destChildren.Count;

        // ═══════════════════════════════════════════════════════════
        // Строим матрицу схожести
        // ═══════════════════════════════════════════════════════════

        var scores = new int[n, m];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                scores[i, j] = CompareChildInFamily(
                    sourceChildren[i],
                    destChildren[j],
                    i,
                    j);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Жадный алгоритм паросочетания
        // ═══════════════════════════════════════════════════════════

        // Собираем все пары с их оценками
        var pairs = new List<(int sourceIdx, int destIdx, int score)>();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                pairs.Add((i, j, scores[i, j]));
            }
        }

        // Сортируем по убыванию оценки
        pairs.Sort((a, b) => b.score.CompareTo(a.score));

        // Жадно выбираем лучшие непересекающиеся пары
        var usedSource = new HashSet<int>();
        var usedDest = new HashSet<int>();
        var mappings = new List<PersonMapping>();

        var threshold = _thresholdCalculator.GetChildThreshold(Math.Min(n, m));

        foreach (var (i, j, score) in pairs)
        {
            if (!usedSource.Contains(i) && !usedDest.Contains(j))
            {
                if (score >= threshold)
                {
                    var sourceChild = sourceChildren[i];
                    var destChild = destChildren[j];

                    _logger?.LogDebug(
                        "Matched child {SourceId} -> {DestId} with score {Score}",
                        sourceChild.Id, destChild.Id, score);

                    mappings.Add(new PersonMapping
                    {
                        SourceId = sourceChild.Id,
                        DestinationId = destChild.Id,
                        MatchScore = score,
                        Level = level,
                        FoundVia = RelationType.Child,
                        FoundInFamilyId = familyId,
                        FoundFromPersonId = fromPersonId,
                        FoundAt = DateTime.UtcNow
                    });

                    usedSource.Add(i);
                    usedDest.Add(j);
                }
            }
        }

        return mappings;
    }

    /// <summary>
    /// Упрощённое сравнение детей в контексте семьи.
    /// Не требует полного fuzzy match — достаточно базовых характеристик.
    /// </summary>
    private int CompareChildInFamily(
        PersonRecord source,
        PersonRecord dest,
        int sourceIndex,
        int destIndex)
    {
        int score = 0;

        // ─────────────────────────────────────────────────────────
        // Пол должен совпадать (обязательно)
        // ─────────────────────────────────────────────────────────
        if (source.Gender != dest.Gender &&
            source.Gender != Gender.Unknown &&
            dest.Gender != Gender.Unknown)
        {
            return 0;  // Разный пол — точно не совпадают
        }

        score += 15;  // Бонус за совпадение пола или Unknown

        // ─────────────────────────────────────────────────────────
        // Имя — основной критерий (используем FuzzyMatcher)
        // ─────────────────────────────────────────────────────────
        var matchResult = _fuzzyMatcher.Compare(source, dest);
        score += (int)(matchResult.Score * 0.6);  // 60% от общего score

        // ─────────────────────────────────────────────────────────
        // Порядок рождения (если индексы похожи)
        // ─────────────────────────────────────────────────────────
        var indexDiff = Math.Abs(sourceIndex - destIndex);
        score += indexDiff switch
        {
            0 or 1 => 10,    // Одинаковый или соседний порядок
            2 => 5,          // Близкий порядок
            _ => 0
        };

        // ─────────────────────────────────────────────────────────
        // Год рождения (мягкая проверка)
        // ─────────────────────────────────────────────────────────
        if (source.BirthYear.HasValue && dest.BirthYear.HasValue)
        {
            var yearDiff = Math.Abs(source.BirthYear.Value - dest.BirthYear.Value);
            score += yearDiff switch
            {
                0 => 15,        // Точное совпадение года
                <= 2 => 10,     // Разница в 1-2 года
                <= 5 => 5,      // Разница в 3-5 лет
                _ => 0
            };
        }

        return Math.Min(score, 100);  // Максимум 100
    }
}
