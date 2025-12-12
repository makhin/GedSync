using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Main orchestrator service for comparing two GEDCOM files
/// Implements the full compare workflow: load, validate anchors, compare individuals and families
/// </summary>
public class GedcomCompareService : IGedcomCompareService
{
    private const int SafetyIterationLimit = 5;

    private readonly ILogger<GedcomCompareService> _logger;
    private readonly IGedcomLoader _gedcomLoader;
    private readonly IIndividualCompareService _individualCompareService;
    private readonly IFamilyCompareService _familyCompareService;

    public GedcomCompareService(
        ILogger<GedcomCompareService> logger,
        IGedcomLoader gedcomLoader,
        IIndividualCompareService individualCompareService,
        IFamilyCompareService familyCompareService)
    {
        _logger = logger;
        _gedcomLoader = gedcomLoader;
        _individualCompareService = individualCompareService;
        _familyCompareService = familyCompareService;
    }

    public CompareResult Compare(string sourceFilePath, string destinationFilePath, CompareOptions options)
    {
        _logger.LogInformation("Starting GEDCOM comparison");
        _logger.LogInformation("Source: {SourceFile}", sourceFilePath);
        _logger.LogInformation("Destination: {DestFile}", destinationFilePath);
        _logger.LogInformation("Options: Depth={Depth}, Threshold={Threshold}, IncludeDeletes={IncludeDeletes}",
            options.NewNodeDepth, options.MatchThreshold, options.IncludeDeleteSuggestions);

        var startTime = DateTime.UtcNow;

        // Step 1: Load both GEDCOM files
        _logger.LogInformation("Loading source GEDCOM file...");
        var sourceResult = _gedcomLoader.Load(sourceFilePath);

        _logger.LogInformation("Loading destination GEDCOM file...");
        var destResult = _gedcomLoader.Load(destinationFilePath);

        // Step 2: Validate anchors
        _logger.LogInformation("Validating anchor persons...");
        if (!sourceResult.Persons.ContainsKey(options.AnchorSourceId))
        {
            throw new ArgumentException(
                $"Anchor person '{options.AnchorSourceId}' not found in source file",
                nameof(options));
        }

        if (!destResult.Persons.ContainsKey(options.AnchorDestinationId))
        {
            throw new ArgumentException(
                $"Anchor person '{options.AnchorDestinationId}' not found in destination file",
                nameof(options));
        }

        var anchorSource = sourceResult.Persons[options.AnchorSourceId];
        var anchorDest = destResult.Persons[options.AnchorDestinationId];

        _logger.LogInformation("Anchor Source: {AnchorSource}", anchorSource.ToString());
        _logger.LogInformation("Anchor Destination: {AnchorDest}", anchorDest.ToString());

        var anchorInfo = new AnchorInfo
        {
            SourceId = options.AnchorSourceId,
            DestinationId = options.AnchorDestinationId,
            GeniProfileId = anchorDest.GeniProfileId,
            MatchConfirmed = true // Assumed - can be validated later
        };

        // Step 3: Determine scope (BFS from anchor)
        // For now, we'll compare all persons. In the future, this can be optimized
        // to only include persons within BFS distance from anchor
        var sourcePersonsInScope = sourceResult.Persons;
        var destPersonsInScope = destResult.Persons;

        _logger.LogInformation("Scope: {SourceCount} source persons, {DestCount} destination persons",
            sourcePersonsInScope.Count, destPersonsInScope.Count);

        var iterationResults = ImmutableList.CreateBuilder<CompareIterationResult>();
        var existingMappings = new Dictionary<string, string>
        {
            [anchorSource.Id] = anchorDest.Id
        };

        IndividualCompareResult? individualResult = null;
        FamilyCompareResult? familyResult = null;
        CompareStatistics? statistics = null;

        for (var iteration = 1; iteration <= SafetyIterationLimit; iteration++)
        {
            _logger.LogInformation("Comparison iteration {Iteration} - individual pass", iteration);
            individualResult = _individualCompareService.CompareIndividuals(
                sourcePersonsInScope,
                destPersonsInScope,
                options,
                existingMappings);

            var newIndividualMappings = AddNewMappings(existingMappings, BuildIdMapping(individualResult));

            _logger.LogInformation("Comparison iteration {Iteration} - family pass", iteration);
            familyResult = _familyCompareService.CompareFamilies(
                sourceResult.Families,
                destResult.Families,
                individualResult,
                options);

            var newFamilyMappings = AddNewMappings(existingMappings, familyResult.NewPersonMappings);
            var totalNewMappings = newIndividualMappings + newFamilyMappings;

            statistics = BuildStatistics(
                sourcePersonsInScope.Count,
                destPersonsInScope.Count,
                sourceResult.Families.Count,
                destResult.Families.Count,
                individualResult,
                familyResult);

            iterationResults.Add(new CompareIterationResult
            {
                Iteration = iteration,
                Individuals = individualResult,
                Families = familyResult,
                Statistics = statistics,
                NewPersonMappings = totalNewMappings
            });

            if (totalNewMappings == 0)
            {
                _logger.LogInformation("No new person mappings in iteration {Iteration}; stopping iterations", iteration);
                break;
            }

            _logger.LogInformation(
                "Iteration {Iteration} discovered {NewMappings} new person mappings", iteration, totalNewMappings);
        }

        if (iterationResults.Count == SafetyIterationLimit && iterationResults[^1].NewPersonMappings > 0)
        {
            _logger.LogWarning("Reached safety iteration limit of {Limit}", SafetyIterationLimit);
        }

        if (statistics == null || individualResult == null || familyResult == null)
        {
            throw new InvalidOperationException("Comparison did not produce any results");
        }

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        _logger.LogInformation("Comparison completed in {Duration}ms", duration.TotalMilliseconds);
        _logger.LogInformation("Results - Individuals: {Matched} matched, {ToUpdate} to update, {ToAdd} to add, {Ambiguous} ambiguous",
            statistics!.Individuals.Matched,
            statistics.Individuals.ToUpdate,
            statistics.Individuals.ToAdd,
            statistics.Individuals.Ambiguous);
        _logger.LogInformation("Results - Families: {Matched} matched, {ToUpdate} to update, {ToAdd} to add",
            statistics.Families.Matched,
            statistics.Families.ToUpdate,
            statistics.Families.ToAdd);

        return new CompareResult
        {
            SourceFile = sourceFilePath,
            DestinationFile = destinationFilePath,
            ComparedAt = startTime,
            Anchors = anchorInfo,
            Options = options,
            Statistics = statistics,
            Individuals = individualResult!,
            Families = familyResult!,
            Iterations = iterationResults.ToImmutable()
        };
    }

    private CompareStatistics BuildStatistics(
        int sourcePersonCount,
        int destinationPersonCount,
        int sourceFamilyCount,
        int destinationFamilyCount,
        IndividualCompareResult individualResult,
        FamilyCompareResult familyResult)
    {
        return new CompareStatistics
        {
            Individuals = new IndividualStats
            {
                TotalSource = sourcePersonCount,
                TotalDestination = destinationPersonCount,
                Matched = individualResult.MatchedNodes.Count,
                ToUpdate = individualResult.NodesToUpdate.Count,
                ToAdd = individualResult.NodesToAdd.Count,
                ToDelete = individualResult.NodesToDelete.Count,
                Ambiguous = individualResult.AmbiguousMatches.Count
            },
            Families = new FamilyStats
            {
                TotalSource = sourceFamilyCount,
                TotalDestination = destinationFamilyCount,
                Matched = familyResult.MatchedFamilies.Count,
                ToUpdate = familyResult.FamiliesToUpdate.Count,
                ToAdd = familyResult.FamiliesToAdd.Count,
                ToDelete = familyResult.FamiliesToDelete.Count
            }
        };
    }

    private ImmutableDictionary<string, string> BuildIdMapping(IndividualCompareResult individualResult)
    {
        var mapping = new Dictionary<string, string>();

        foreach (var matched in individualResult.MatchedNodes)
        {
            mapping[matched.SourceId] = matched.DestinationId;
        }

        foreach (var toUpdate in individualResult.NodesToUpdate)
        {
            mapping[toUpdate.SourceId] = toUpdate.DestinationId;
        }

        return mapping.ToImmutableDictionary();
    }

    private int AddNewMappings(Dictionary<string, string> existingMappings, ImmutableDictionary<string, string> newPersonMappings)
    {
        var added = 0;

        foreach (var (sourceId, destId) in newPersonMappings)
        {
            if (existingMappings.ContainsKey(sourceId) || existingMappings.ContainsValue(destId))
            {
                continue;
            }

            existingMappings[sourceId] = destId;
            added++;
        }

        return added;
    }
}
