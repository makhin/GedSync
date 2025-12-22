using GedcomGeniSync.Core.Models;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Core.Services.Interactive;
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
    private readonly ConfirmedMappingsStore? _confirmedMappingsStore;
    private readonly IInteractiveConfirmation? _interactiveConfirmation;
    private readonly ILogger<WaveCompareService> _logger;

    private WaveCompareOptions? _currentOptions;
    private GedcomLoadResult? _currentSourceLoadResult;
    private GedcomLoadResult? _currentDestLoadResult;

    public WaveCompareService(
        IFuzzyMatcherService fuzzyMatcher,
        ILogger<WaveCompareService> logger,
        ConfirmedMappingsStore? confirmedMappingsStore = null,
        IInteractiveConfirmation? interactiveConfirmation = null)
    {
        _fuzzyMatcher = fuzzyMatcher;
        _logger = logger;
        _confirmedMappingsStore = confirmedMappingsStore;
        _interactiveConfirmation = interactiveConfirmation;
        _treeIndexer = new TreeIndexer(logger: null);
        _familyMatcher = new FamilyMatcher(logger: null, fuzzyMatcher: _fuzzyMatcher);
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

        // Сохраняем для использования в интерактивном режиме
        _currentOptions = options;
        _currentSourceLoadResult = sourceLoadResult;
        _currentDestLoadResult = destLoadResult;

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

        // Загрузить подтверждённые соответствия (если указан файл)
        ConfirmedMappingsFile? confirmedMappings = null;
        HashSet<(string, string)> rejectedPairs = new();

        if (!string.IsNullOrEmpty(options.ConfirmedMappingsFile) && _confirmedMappingsStore != null)
        {
            confirmedMappings = _confirmedMappingsStore.LoadMappings(options.ConfirmedMappingsFile);
            if (confirmedMappings != null)
            {
                rejectedPairs = _confirmedMappingsStore.GetRejectedPairs(confirmedMappings);
                _logger.LogInformation(
                    "Loaded {ConfirmedCount} confirmed mappings, {RejectedCount} rejected pairs",
                    confirmedMappings.Mappings.Count(m => m.Type == ConfirmationType.Confirmed),
                    rejectedPairs.Count);
            }
        }

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

        // Добавить подтверждённые соответствия как дополнительные якоря
        if (confirmedMappings != null)
        {
            var confirmedAnchors = _confirmedMappingsStore!.GetConfirmedAnchors(confirmedMappings);
            foreach (var (sourceId, destId) in confirmedAnchors)
            {
                // Проверяем что эта персона существует в обоих деревьях
                if (!sourceLoadResult.Persons.ContainsKey(sourceId) ||
                    !destLoadResult.Persons.ContainsKey(destId))
                {
                    _logger.LogWarning(
                        "Confirmed mapping {SourceId} -> {DestId} skipped: person not found in trees",
                        sourceId, destId);
                    continue;
                }

                // Пропускаем если уже есть в mappings (якорь имеет приоритет)
                if (mappings.ContainsKey(sourceId))
                {
                    _logger.LogDebug("Confirmed mapping {SourceId} -> {DestId} skipped: already mapped as anchor", sourceId, destId);
                    continue;
                }

                mappings[sourceId] = new PersonMapping
                {
                    SourceId = sourceId,
                    DestinationId = destId,
                    MatchScore = 100,
                    Level = 0,
                    FoundVia = RelationType.Anchor,
                    FoundAt = DateTime.UtcNow
                };

                queue.Enqueue((sourceId, 0));
                processed.Add(sourceId);

                _logger.LogDebug("Added confirmed mapping as anchor: {SourceId} -> {DestId}", sourceId, destId);
            }
        }

        // Статистика по уровням
        var levelStats = new List<LevelStatistics>();
        var validationIssues = new List<ValidationIssue>();

        // Детализированное логирование
        var detailedLog = new WaveCompareLog
        {
            SourceFile = "source.ged", // TODO: Pass actual file paths
            DestinationFile = "dest.ged",
            AnchorSourceId = anchorSourceId,
            AnchorDestId = anchorDestId,
            MaxLevel = options.MaxLevel,
            Strategy = options.ThresholdStrategy,
            StartTime = startTime,
            EndTime = DateTime.UtcNow, // Will update at end
            Levels = new List<WaveLevelLog>(),
            Result = null! // Will set at end
        };

        var currentLevelLog = new Dictionary<int, WaveLevelLog>();

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

            // Проверяем, сопоставлена ли персона
            if (!mappings.ContainsKey(currentSourceId))
            {
                // Персона НЕ сопоставлена, но мы добавили её для исследования потомков
                _logger.LogDebug(
                    "Processing unmatched person {PersonId} at level {Level} for exploration only",
                    currentSourceId,
                    level);

                // Для несопоставленной персоны мы НЕ можем обрабатывать её семьи,
                // так как у нас нет соответствующих destination families.
                // Пропускаем её - она уже в processed, поэтому не будет добавлена снова.
                continue;
            }

            // Получаем сопоставленный ID в destination
            var currentDestId = mappings[currentSourceId].DestinationId;

            var newMappingsThisLevel = 0;
            var familiesProcessed = 0;

            // Инициализируем лог для текущего уровня если нужно
            if (!currentLevelLog.ContainsKey(level))
            {
                currentLevelLog[level] = new WaveLevelLog
                {
                    Level = level,
                    PersonsProcessedAtLevel = 0,
                    FamiliesExaminedAtLevel = 0,
                    NewMappingsAtLevel = 0,
                    PersonsProcessed = new List<PersonProcessingLog>()
                };
            }

            // Создаем лог обработки текущей персоны
            var sourcePerson = sourceTree.PersonsById[currentSourceId];
            var destPerson = destTree.PersonsById[currentDestId];
            var personLog = new PersonProcessingLog
            {
                SourceId = currentSourceId,
                SourceName = sourcePerson.ToString(),
                DestinationId = currentDestId,
                DestinationName = destPerson.ToString(),
                MappedVia = mappings[currentSourceId].FoundVia,
                Level = level,
                FamiliesAsSpouse = new List<FamilyMatchAttemptLog>(),
                FamiliesAsChild = new List<FamilyMatchAttemptLog>()
            };

            currentLevelLog[level] = currentLevelLog[level] with
            {
                PersonsProcessedAtLevel = currentLevelLog[level].PersonsProcessedAtLevel + 1
            };

            // ─────────────────────────────────────────────────────────
            // Обрабатываем семьи, где персона — СУПРУГ/РОДИТЕЛЬ
            // ─────────────────────────────────────────────────────────

            var sourceFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(sourceTree, currentSourceId);
            var destFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(destTree, currentDestId);

            foreach (var sourceFamily in sourceFamiliesAsSpouse)
            {
                var (destFamily, familyLog) = _familyMatcher.FindMatchingFamilyWithLog(
                    sourceFamily,
                    destFamiliesAsSpouse,
                    mappings,
                    sourceTree,
                    destTree);

                personLog.FamiliesAsSpouse.Add(familyLog);

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

                    ProcessMatchedFamily(
                        sourceFamily,
                        destFamily,
                        context,
                        familyMemberMatcher,
                        sourceTree,
                        destTree,
                        mappings,
                        processed,
                        queue,
                        validationIssues,
                        level,
                        ref newMappingsThisLevel);
                }
            }

            // ─────────────────────────────────────────────────────────
            // Обрабатываем семьи, где персона — РЕБЁНОК
            // ─────────────────────────────────────────────────────────

            var sourceFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(sourceTree, currentSourceId);
            var destFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(destTree, currentDestId);

            foreach (var sourceFamily in sourceFamiliesAsChild)
            {
                var (destFamily, familyLog) = _familyMatcher.FindMatchingFamilyWithLog(
                    sourceFamily,
                    destFamiliesAsChild,
                    mappings,
                    sourceTree,
                    destTree);

                personLog.FamiliesAsChild.Add(familyLog);

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

                    ProcessMatchedFamily(
                        sourceFamily,
                        destFamily,
                        context,
                        familyMemberMatcher,
                        sourceTree,
                        destTree,
                        mappings,
                        processed,
                        queue,
                        validationIssues,
                        level,
                        ref newMappingsThisLevel);
                }
            }

            // Добавляем лог персоны к лог-уровню
            var updatedPersons = currentLevelLog[level].PersonsProcessed.ToList();
            updatedPersons.Add(personLog);
            currentLevelLog[level] = currentLevelLog[level] with
            {
                PersonsProcessed = updatedPersons,
                FamiliesExaminedAtLevel = currentLevelLog[level].FamiliesExaminedAtLevel + familiesProcessed,
                NewMappingsAtLevel = currentLevelLog[level].NewMappingsAtLevel + newMappingsThisLevel
            };

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

        if (options.ResolveConflicts)
        {
            _logger.LogInformation("Starting conflict resolution phase...");
            var resolver = new MappingConflictResolver(_fuzzyMatcher, _logger);
            resolver.ResolveConflicts(
                mappings,
                sourceLoadResult,
                destLoadResult);
        }

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

        var result = new WaveCompareResult
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

        // Завершаем детализированный лог
        detailedLog = detailedLog with
        {
            EndTime = DateTime.UtcNow,
            Levels = currentLevelLog.Values.OrderBy(l => l.Level).ToList(),
            Result = result
        };

        // Сохраняем для последующего доступа
        _lastDetailedLog = detailedLog;

        return result;
    }

    /// <summary>
    /// Получить детализированный лог последнего сравнения.
    /// </summary>
    public WaveCompareLog? GetDetailedLog()
    {
        return _lastDetailedLog;
    }

    private WaveCompareLog? _lastDetailedLog;

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

    /// <summary>
    /// Обработать найденную семью: сопоставить членов и добавить в очередь.
    /// Если хотя бы один член семьи был сопоставлен, добавляет несопоставленных членов
    /// в очередь для исследования их потомков.
    /// </summary>
    private void ProcessMatchedFamily(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        FamilyMatchContext context,
        FamilyMemberMatcher familyMemberMatcher,
        TreeGraph sourceTree,
        TreeGraph destTree,
        Dictionary<string, PersonMapping> mappings,
        HashSet<string> processed,
        Queue<(string sourceId, int level)> queue,
        List<ValidationIssue> validationIssues,
        int level,
        ref int newMappingsThisLevel)
    {
        var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

        // Собираем всех членов source семьи
        var allSourceFamilyMembers = new HashSet<string>();
        if (sourceFamily.HusbandId != null) allSourceFamilyMembers.Add(sourceFamily.HusbandId);
        if (sourceFamily.WifeId != null) allSourceFamilyMembers.Add(sourceFamily.WifeId);
        foreach (var childId in sourceFamily.ChildIds)
            allSourceFamilyMembers.Add(childId);

        bool anyMemberMatched = false;

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
                    // Check interactive confirmation if score is low
                    // Create local copy since 'mapping' is foreach iteration variable
                    var currentMapping = mapping;
                    string foundViaDescription = $"{currentMapping.FoundVia} от \"{context.FromPersonId}\"";
                    bool shouldAdd = TryInteractiveConfirmation(
                        ref currentMapping,
                        foundViaDescription,
                        mappings,
                        out bool shouldSave);

                    if (shouldAdd)
                    {
                        mappings[currentMapping.SourceId] = currentMapping;
                        queue.Enqueue((currentMapping.SourceId, level + 1));
                        processed.Add(currentMapping.SourceId);
                        newMappingsThisLevel++;
                        anyMemberMatched = true;
                        allSourceFamilyMembers.Remove(currentMapping.SourceId);

                        _logger.LogDebug(
                            "Added mapping {SourceId} -> {DestId} via {RelationType} at level {Level}",
                            currentMapping.SourceId,
                            currentMapping.DestinationId,
                            currentMapping.FoundVia,
                            currentMapping.Level);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Rejected mapping {SourceId} -> {DestId} via interactive confirmation or low score",
                            currentMapping.SourceId,
                            currentMapping.DestinationId);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Rejected mapping {SourceId} -> {DestId} due to validation failures",
                        mapping.SourceId,
                        mapping.DestinationId);
                }
            }
            else
            {
                // Член семьи уже был обработан ранее
                allSourceFamilyMembers.Remove(mapping.SourceId);
                anyMemberMatched = true;
            }
        }

        // Добавляем несопоставленных членов семьи в очередь для исследования,
        // ТОЛЬКО если хотя бы один член семьи был успешно сопоставлен
        if (anyMemberMatched)
        {
            foreach (var unmatchedMemberId in allSourceFamilyMembers)
            {
                if (!processed.Contains(unmatchedMemberId))
                {
                    queue.Enqueue((unmatchedMemberId, level + 1));
                    processed.Add(unmatchedMemberId);

                    _logger.LogDebug(
                        "Added unmatched family member {SourceId} to queue for exploration at level {Level}",
                        unmatchedMemberId,
                        level + 1);
                }
            }
        }
    }

    /// <summary>
    /// Try to confirm mapping interactively if score is low
    /// Returns true if mapping should be added, false if rejected/skipped
    /// Updates the mapping reference with user's selected candidate if applicable
    /// </summary>
    private bool TryInteractiveConfirmation(
        ref PersonMapping mapping,
        string foundVia,
        Dictionary<string, PersonMapping> existingMappings,
        out bool shouldSave)
    {
        shouldSave = false;

        // Check if interactive mode is enabled
        if (_currentOptions == null || !_currentOptions.Interactive ||
            _interactiveConfirmation == null ||
            _confirmedMappingsStore == null ||
            _currentSourceLoadResult == null ||
            _currentDestLoadResult == null)
        {
            return true; // No interactive mode, accept by default
        }

        int score = mapping.MatchScore;
        int lowThreshold = _currentOptions.LowConfidenceThreshold;
        int minThreshold = _currentOptions.MinConfidenceThreshold;

        // High confidence - auto accept
        if (score >= lowThreshold)
        {
            return true;
        }

        // Too low - auto reject
        if (score < minThreshold)
        {
            _logger.LogInformation(
                "Auto-rejected mapping {SourceId} -> {DestId} with score {Score} (below min threshold {MinThreshold})",
                mapping.SourceId, mapping.DestinationId, score, minThreshold);
            return false;
        }

        // Between thresholds - ask user
        _logger.LogInformation(
            "Requesting user confirmation for {SourceId} with score {Score} (asking user for selection)",
            mapping.SourceId, score);

        try
        {
            var sourcePerson = _currentSourceLoadResult.Persons[mapping.SourceId];

            // Find all potential candidates from destination tree
            var allDestPersons = _currentDestLoadResult.Persons.Values;

            // Use FindMatches to get all candidates above minThreshold
            var matchCandidates = _fuzzyMatcher.FindMatches(
                sourcePerson,
                allDestPersons,
                minScore: minThreshold);

            // Convert to interactive candidates with detailed breakdown
            var candidates = matchCandidates
                .Take(_currentOptions.MaxCandidates)
                .Select(mc => ConvertToInteractiveCandidate(mc))
                .ToList();

            // If no candidates found (shouldn't happen as we already have a mapping),
            // fall back to the original mapping
            if (candidates.Count == 0)
            {
                _logger.LogWarning(
                    "No candidates found for {SourceId}, accepting original mapping",
                    mapping.SourceId);
                return true;
            }

            var request = new Models.Interactive.InteractiveConfirmationRequest
            {
                SourcePerson = sourcePerson,
                Candidates = candidates,
                FoundVia = foundVia,
                Level = mapping.Level,
                MaxCandidates = _currentOptions.MaxCandidates
            };

            var result = _interactiveConfirmation.AskUser(request);

            // If user confirmed, update mapping with selected candidate
            if (result.Decision == Models.Interactive.UserDecision.Confirmed &&
                result.SelectedScore.HasValue &&
                result.SelectedScore.Value >= 1 &&
                result.SelectedScore.Value <= candidates.Count)
            {
                var selectedCandidate = candidates[result.SelectedScore.Value - 1];
                mapping = mapping with
                {
                    DestinationId = selectedCandidate.Person.Id,
                    MatchScore = selectedCandidate.Score
                };

                _logger.LogInformation(
                    "User selected candidate {DestId} with score {Score} for {SourceId}",
                    selectedCandidate.Person.Id,
                    selectedCandidate.Score,
                    mapping.SourceId);
            }

            // Save user decision
            shouldSave = true;
            var userMapping = new UserConfirmedMapping
            {
                SourceId = mapping.SourceId,
                DestinationId = result.Decision == Models.Interactive.UserDecision.Confirmed
                    ? mapping.DestinationId
                    : null,
                Type = result.Decision switch
                {
                    Models.Interactive.UserDecision.Confirmed => ConfirmationType.Confirmed,
                    Models.Interactive.UserDecision.Rejected => ConfirmationType.Rejected,
                    Models.Interactive.UserDecision.Skipped => ConfirmationType.Skipped,
                    _ => ConfirmationType.Skipped
                },
                ConfirmedAt = DateTime.UtcNow,
                OriginalScore = score
            };

            // Save to file
            if (!string.IsNullOrEmpty(_currentOptions.ConfirmedMappingsFile))
            {
                _confirmedMappingsStore.AddOrUpdateMapping(
                    _currentOptions.ConfirmedMappingsFile,
                    userMapping,
                    sourceFile: "source.ged", // TODO: Get actual file names
                    destinationFile: "dest.ged");
            }

            return result.Decision == Models.Interactive.UserDecision.Confirmed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during interactive confirmation");
            return false;
        }
    }

    /// <summary>
    /// Convert MatchCandidate to Interactive.CandidateMatch with detailed breakdown
    /// </summary>
    private Models.Interactive.CandidateMatch ConvertToInteractiveCandidate(MatchCandidate matchCandidate)
    {
        var breakdown = BuildScoreBreakdown(
            matchCandidate.Source,
            matchCandidate.Target,
            matchCandidate.Reasons.ToList());

        return new Models.Interactive.CandidateMatch
        {
            Person = matchCandidate.Target,
            Score = (int)Math.Round(matchCandidate.Score),
            Breakdown = breakdown
        };
    }

    /// <summary>
    /// Build ScoreBreakdown from MatchReasons and person records
    /// </summary>
    private Models.Interactive.ScoreBreakdown BuildScoreBreakdown(
        PersonRecord source,
        PersonRecord target,
        List<MatchReason> reasons)
    {
        // Extract scores from MatchReasons
        var firstNameReason = reasons.FirstOrDefault(r => r.Field == "FirstName");
        var lastNameReason = reasons.FirstOrDefault(r => r.Field == "LastName");
        var maidenNameReason = reasons.FirstOrDefault(r => r.Field == "MaidenName");
        var birthDateReason = reasons.FirstOrDefault(r => r.Field == "BirthDate");
        var birthPlaceReason = reasons.FirstOrDefault(r => r.Field == "BirthPlace");
        var genderReason = reasons.FirstOrDefault(r => r.Field == "Gender");

        // Calculate individual field scores (0-1 range)
        // Points are already weighted, so we need to divide by weight to get raw score
        // Use standard weights from MatchingOptions defaults
        const double firstNameWeight = 25.0;
        const double lastNameWeight = 20.0;
        const double birthDateWeight = 15.0;
        const double birthPlaceWeight = 10.0;

        var firstNameScore = firstNameReason != null
            ? Math.Min(1.0, firstNameReason.Points / firstNameWeight)
            : 0.0;

        var lastNameScore = lastNameReason != null || maidenNameReason != null
            ? Math.Min(1.0, ((lastNameReason?.Points ?? 0) + (maidenNameReason?.Points ?? 0)) / lastNameWeight)
            : 0.0;

        var birthDateScore = birthDateReason != null
            ? Math.Min(1.0, birthDateReason.Points / birthDateWeight)
            : 0.0;

        var birthPlaceScore = birthPlaceReason != null
            ? Math.Min(1.0, birthPlaceReason.Points / birthPlaceWeight)
            : 0.0;

        var genderScore = genderReason != null
            ? (genderReason.Points >= 0 ? 1.0 : 0.0)
            : 1.0;

        // Count family relations matches
        int parentsMatching = 0;
        int parentsTotal = 0;
        int childrenMatching = 0;
        int childrenTotal = 0;
        int siblingsMatching = 0;
        int siblingsTotal = 0;
        bool spouseMatches = false;

        // Parents
        if (!string.IsNullOrEmpty(source.FatherId) || !string.IsNullOrEmpty(target.FatherId))
        {
            parentsTotal++;
            if (!string.IsNullOrEmpty(source.FatherId) && !string.IsNullOrEmpty(target.FatherId))
            {
                // Check if fathers match (simplified - could use fuzzy comparison)
                if (_currentSourceLoadResult != null && _currentDestLoadResult != null)
                {
                    var sourceFather = _currentSourceLoadResult.Persons.GetValueOrDefault(source.FatherId);
                    var targetFather = _currentDestLoadResult.Persons.GetValueOrDefault(target.FatherId);
                    if (sourceFather != null && targetFather != null)
                    {
                        var fatherMatch = _fuzzyMatcher.Compare(sourceFather, targetFather);
                        if (fatherMatch.Score >= 70)
                            parentsMatching++;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(source.MotherId) || !string.IsNullOrEmpty(target.MotherId))
        {
            parentsTotal++;
            if (!string.IsNullOrEmpty(source.MotherId) && !string.IsNullOrEmpty(target.MotherId))
            {
                if (_currentSourceLoadResult != null && _currentDestLoadResult != null)
                {
                    var sourceMother = _currentSourceLoadResult.Persons.GetValueOrDefault(source.MotherId);
                    var targetMother = _currentDestLoadResult.Persons.GetValueOrDefault(target.MotherId);
                    if (sourceMother != null && targetMother != null)
                    {
                        var motherMatch = _fuzzyMatcher.Compare(sourceMother, targetMother);
                        if (motherMatch.Score >= 70)
                            parentsMatching++;
                    }
                }
            }
        }

        // Children
        childrenTotal = Math.Max(source.ChildrenIds.Count, target.ChildrenIds.Count);
        if (childrenTotal > 0 && _currentSourceLoadResult != null && _currentDestLoadResult != null)
        {
            foreach (var sourceChildId in source.ChildrenIds)
            {
                var sourceChild = _currentSourceLoadResult.Persons.GetValueOrDefault(sourceChildId);
                if (sourceChild == null) continue;

                foreach (var targetChildId in target.ChildrenIds)
                {
                    var targetChild = _currentDestLoadResult.Persons.GetValueOrDefault(targetChildId);
                    if (targetChild == null) continue;

                    var childMatch = _fuzzyMatcher.Compare(sourceChild, targetChild);
                    if (childMatch.Score >= 70)
                    {
                        childrenMatching++;
                        break;
                    }
                }
            }
        }

        // Siblings
        siblingsTotal = Math.Max(source.SiblingIds.Count, target.SiblingIds.Count);
        if (siblingsTotal > 0 && _currentSourceLoadResult != null && _currentDestLoadResult != null)
        {
            foreach (var sourceSiblingId in source.SiblingIds)
            {
                var sourceSibling = _currentSourceLoadResult.Persons.GetValueOrDefault(sourceSiblingId);
                if (sourceSibling == null) continue;

                foreach (var targetSiblingId in target.SiblingIds)
                {
                    var targetSibling = _currentDestLoadResult.Persons.GetValueOrDefault(targetSiblingId);
                    if (targetSibling == null) continue;

                    var siblingMatch = _fuzzyMatcher.Compare(sourceSibling, targetSibling);
                    if (siblingMatch.Score >= 70)
                    {
                        siblingsMatching++;
                        break;
                    }
                }
            }
        }

        // Spouses
        if (source.SpouseIds.Any() && target.SpouseIds.Any() &&
            _currentSourceLoadResult != null && _currentDestLoadResult != null)
        {
            foreach (var sourceSpouseId in source.SpouseIds)
            {
                var sourceSpouse = _currentSourceLoadResult.Persons.GetValueOrDefault(sourceSpouseId);
                if (sourceSpouse == null) continue;

                foreach (var targetSpouseId in target.SpouseIds)
                {
                    var targetSpouse = _currentDestLoadResult.Persons.GetValueOrDefault(targetSpouseId);
                    if (targetSpouse == null) continue;

                    var spouseMatch = _fuzzyMatcher.Compare(sourceSpouse, targetSpouse);
                    if (spouseMatch.Score >= 70)
                    {
                        spouseMatches = true;
                        break;
                    }
                }

                if (spouseMatches) break;
            }
        }

        return new Models.Interactive.ScoreBreakdown
        {
            FirstNameScore = firstNameScore,
            FirstNameDetails = firstNameReason?.Details,
            LastNameScore = lastNameScore,
            LastNameDetails = lastNameReason?.Details ?? maidenNameReason?.Details,
            BirthDateScore = birthDateScore,
            BirthDateDetails = birthDateReason?.Details,
            BirthPlaceScore = birthPlaceScore,
            BirthPlaceDetails = birthPlaceReason?.Details,
            GenderScore = genderScore,
            ParentsMatching = parentsMatching,
            ParentsTotal = parentsTotal,
            ChildrenMatching = childrenMatching,
            ChildrenTotal = childrenTotal,
            SiblingsMatching = siblingsMatching,
            SiblingsTotal = siblingsTotal,
            SpouseMatches = spouseMatches
        };
    }
}
