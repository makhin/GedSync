using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;
using RelationType = GedcomGeniSync.Core.Models.Wave.RelationType;
using AnchorInfo = GedcomGeniSync.Core.Models.Wave.AnchorInfo;
using CompareStatistics = GedcomGeniSync.Core.Models.Wave.CompareStatistics;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Главный оркестратор волнового алгоритма сравнения генеалогических деревьев.
/// Использует BFS для распространения сопоставлений от якорной персоны по связям.
/// </summary>
public class WaveCompareService
{
    private readonly IFuzzyMatcherService _fuzzyMatcher;
    private readonly TreeIndexer _treeIndexer;
    private readonly FamilyMatcher _familyMatcher;
    private readonly WaveMappingValidator _validator;
    private readonly ILogger<WaveCompareService> _logger;

    public WaveCompareService(
        IFuzzyMatcherService fuzzyMatcher,
        ILogger<WaveCompareService> logger)
    {
        _fuzzyMatcher = fuzzyMatcher;
        _logger = logger;
        _treeIndexer = new TreeIndexer(logger: null);
        _familyMatcher = new FamilyMatcher(logger: null);
        _validator = new WaveMappingValidator(logger: null);
    }

    /// <summary>
    /// Сравнить два GEDCOM дерева используя волновой алгоритм.
    /// </summary>
    public WaveCompareResult Compare(
        GedcomLoadResult sourceLoadResult,
        GedcomLoadResult destLoadResult,
        string anchorSourceId,
        string anchorDestId,
        WaveCompareOptions options)
    {
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Starting wave compare: Source={SourceCount} persons, Dest={DestCount} persons, MaxLevel={MaxLevel}",
            sourceLoadResult.Persons.Count,
            destLoadResult.Persons.Count,
            options.MaxLevel);

        // ═══════════════════════════════════════════════════════════
        // ИНИЦИАЛИЗАЦИЯ
        // ═══════════════════════════════════════════════════════════

        // Построить графы с индексами
        var sourceTree = _treeIndexer.BuildIndex(sourceLoadResult);
        var destTree = _treeIndexer.BuildIndex(destLoadResult);

        // Настроить FuzzyMatcher для использования персон
        _fuzzyMatcher.SetPersonDictionaries(
            sourceLoadResult.Persons,
            destLoadResult.Persons);

        // Создать ThresholdCalculator и FamilyMemberMatcher
        var thresholdCalculator = new ThresholdCalculator(
            options.ThresholdStrategy,
            options.BaseThreshold);

        var familyMemberMatcher = new FamilyMemberMatcher(
            _fuzzyMatcher,
            thresholdCalculator,
            logger: null);

        // Словарь сопоставлений: sourceId -> PersonMapping
        var mappings = new Dictionary<string, PersonMapping>();

        // Добавляем якорь как первое сопоставление
        mappings[anchorSourceId] = new PersonMapping
        {
            SourceId = anchorSourceId,
            DestinationId = anchorDestId,
            MatchScore = 100,
            Level = 0,
            FoundVia = RelationType.Anchor,
            FoundAt = DateTime.UtcNow
        };

        // BFS очередь: (sourcePersonId, level)
        var queue = new Queue<(string personId, int level)>();
        queue.Enqueue((anchorSourceId, 0));

        // Множество обработанных персон
        var processed = new HashSet<string> { anchorSourceId };

        // Статистика по уровням
        var levelStats = new List<LevelStatistics>();
        var validationIssues = new List<ValidationIssue>();

        // ═══════════════════════════════════════════════════════════
        // ОСНОВНОЙ ЦИКЛ BFS
        // ═══════════════════════════════════════════════════════════

        while (queue.Count > 0)
        {
            var levelStartTime = DateTime.UtcNow;
            var (currentSourceId, level) = queue.Dequeue();

            _logger.LogDebug("Processing person {PersonId} at level {Level}", currentSourceId, level);

            // Проверяем ограничение глубины
            if (level >= options.MaxLevel)
            {
                _logger.LogDebug("Reached max level {MaxLevel}, stopping expansion", options.MaxLevel);
                continue;
            }

            // Получаем сопоставленный ID в destination
            var currentDestId = mappings[currentSourceId].DestinationId;

            var newMappingsThisLevel = 0;
            var familiesProcessed = 0;

            // ─────────────────────────────────────────────────────────
            // Обрабатываем семьи, где персона — СУПРУГ/РОДИТЕЛЬ
            // ─────────────────────────────────────────────────────────

            var sourceFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(sourceTree, currentSourceId);
            var destFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(destTree, currentDestId);

            foreach (var sourceFamily in sourceFamiliesAsSpouse)
            {
                var destFamily = _familyMatcher.FindMatchingFamily(
                    sourceFamily,
                    destFamiliesAsSpouse,
                    mappings);

                if (destFamily != null)
                {
                    familiesProcessed++;

                    var context = new FamilyMatchContext
                    {
                        SourceFamily = sourceFamily,
                        DestinationFamily = destFamily,
                        ExistingMappings = mappings.ToDictionary(kv => kv.Key, kv => kv.Value.DestinationId),
                        CurrentLevel = level,
                        FromPersonId = currentSourceId
                    };

                    var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

                    foreach (var mapping in newMappings)
                    {
                        if (!processed.Contains(mapping.SourceId))
                        {
                            // Валидируем новое сопоставление
                            var validationResult = _validator.ValidateMapping(
                                mapping,
                                mappings,
                                sourceTree,
                                destTree);

                            // Добавляем issues к общему списку
                            validationIssues.AddRange(validationResult.Issues);

                            // Добавляем mapping только если валидация прошла
                            if (validationResult.IsValid)
                            {
                                mappings[mapping.SourceId] = mapping;
                                queue.Enqueue((mapping.SourceId, level + 1));
                                processed.Add(mapping.SourceId);
                                newMappingsThisLevel++;

                                _logger.LogDebug(
                                    "Added mapping {SourceId} -> {DestId} via {RelationType} at level {Level}",
                                    mapping.SourceId,
                                    mapping.DestinationId,
                                    mapping.FoundVia,
                                    mapping.Level);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Rejected mapping {SourceId} -> {DestId} due to validation failures",
                                    mapping.SourceId,
                                    mapping.DestinationId);
                            }
                        }
                    }
                }
            }

            // ─────────────────────────────────────────────────────────
            // Обрабатываем семьи, где персона — РЕБЁНОК
            // ─────────────────────────────────────────────────────────

            var sourceFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(sourceTree, currentSourceId);
            var destFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(destTree, currentDestId);

            foreach (var sourceFamily in sourceFamiliesAsChild)
            {
                var destFamily = _familyMatcher.FindMatchingFamily(
                    sourceFamily,
                    destFamiliesAsChild,
                    mappings);

                if (destFamily != null)
                {
                    familiesProcessed++;

                    var context = new FamilyMatchContext
                    {
                        SourceFamily = sourceFamily,
                        DestinationFamily = destFamily,
                        ExistingMappings = mappings.ToDictionary(kv => kv.Key, kv => kv.Value.DestinationId),
                        CurrentLevel = level,
                        FromPersonId = currentSourceId
                    };

                    var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

                    foreach (var mapping in newMappings)
                    {
                        if (!processed.Contains(mapping.SourceId))
                        {
                            // Валидируем новое сопоставление
                            var validationResult = _validator.ValidateMapping(
                                mapping,
                                mappings,
                                sourceTree,
                                destTree);

                            // Добавляем issues к общему списку
                            validationIssues.AddRange(validationResult.Issues);

                            // Добавляем mapping только если валидация прошла
                            if (validationResult.IsValid)
                            {
                                mappings[mapping.SourceId] = mapping;
                                queue.Enqueue((mapping.SourceId, level + 1));
                                processed.Add(mapping.SourceId);
                                newMappingsThisLevel++;

                                _logger.LogDebug(
                                    "Added mapping {SourceId} -> {DestId} via {RelationType} at level {Level}",
                                    mapping.SourceId,
                                    mapping.DestinationId,
                                    mapping.FoundVia,
                                    mapping.Level);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Rejected mapping {SourceId} -> {DestId} due to validation failures",
                                    mapping.SourceId,
                                    mapping.DestinationId);
                            }
                        }
                    }
                }
            }

            // Собираем статистику по уровню
            var levelDuration = DateTime.UtcNow - levelStartTime;
            var existingStat = levelStats.FirstOrDefault(s => s.Level == level);

            if (existingStat == null)
            {
                levelStats.Add(new LevelStatistics
                {
                    Level = level,
                    PersonsProcessed = 1,
                    NewMappingsFound = newMappingsThisLevel,
                    FamiliesProcessed = familiesProcessed,
                    Duration = levelDuration
                });
            }
            else
            {
                var index = levelStats.IndexOf(existingStat);
                levelStats[index] = existingStat with
                {
                    PersonsProcessed = existingStat.PersonsProcessed + 1,
                    NewMappingsFound = existingStat.NewMappingsFound + newMappingsThisLevel,
                    FamiliesProcessed = existingStat.FamiliesProcessed + familiesProcessed,
                    Duration = existingStat.Duration + levelDuration
                };
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ФОРМИРОВАНИЕ РЕЗУЛЬТАТА
        // ═══════════════════════════════════════════════════════════

        var totalDuration = DateTime.UtcNow - startTime;

        // Найти несопоставленные персоны
        var unmatchedSource = FindUnmatchedSource(sourceLoadResult, mappings);
        var unmatchedDest = FindUnmatchedDest(destLoadResult, mappings);

        // Общая статистика
        var statistics = new CompareStatistics
        {
            TotalSourcePersons = sourceLoadResult.Persons.Count,
            TotalDestinationPersons = destLoadResult.Persons.Count,
            TotalMappings = mappings.Count,
            UnmatchedSourceCount = unmatchedSource.Count,
            UnmatchedDestinationCount = unmatchedDest.Count,
            TotalDuration = totalDuration,
            ValidationIssuesCount = validationIssues.Count
        };

        _logger.LogInformation(
            "Wave compare completed: Mapped {MappedCount}/{TotalSource} persons in {Duration:g}",
            mappings.Count,
            sourceLoadResult.Persons.Count,
            totalDuration);

        return new WaveCompareResult
        {
            SourceFile = "source.ged", // TODO: Pass actual file paths
            DestinationFile = "dest.ged",
            ComparedAt = DateTime.UtcNow,
            Anchors = new AnchorInfo
            {
                SourceId = anchorSourceId,
                DestinationId = anchorDestId,
                SourcePersonSummary = sourceLoadResult.Persons[anchorSourceId].ToString(),
                DestinationPersonSummary = destLoadResult.Persons[anchorDestId].ToString()
            },
            Options = options,
            Mappings = mappings.Values.ToList(),
            UnmatchedSource = unmatchedSource,
            UnmatchedDestination = unmatchedDest,
            ValidationIssues = validationIssues,
            StatisticsByLevel = levelStats,
            Statistics = statistics
        };
    }

    private List<UnmatchedPerson> FindUnmatchedSource(
        GedcomLoadResult loadResult,
        Dictionary<string, PersonMapping> mappings)
    {
        return loadResult.Persons
            .Where(kv => !mappings.ContainsKey(kv.Key))
            .Select(kv => new UnmatchedPerson
            {
                Id = kv.Key,
                PersonSummary = kv.Value.ToString()
            })
            .ToList();
    }

    private List<UnmatchedPerson> FindUnmatchedDest(
        GedcomLoadResult loadResult,
        Dictionary<string, PersonMapping> mappings)
    {
        var mappedDestIds = new HashSet<string>(
            mappings.Values.Select(m => m.DestinationId));

        return loadResult.Persons
            .Where(kv => !mappedDestIds.Contains(kv.Key))
            .Select(kv => new UnmatchedPerson
            {
                Id = kv.Key,
                PersonSummary = kv.Value.ToString()
            })
            .ToList();
    }
}
