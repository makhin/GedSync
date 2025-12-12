using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Immutable;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing family (FAM) records between source and destination GEDCOM files
/// Matches families based on matched spouse IDs
/// </summary>
public class FamilyCompareService : IFamilyCompareService
{
    private readonly ILogger<FamilyCompareService> _logger;

    public FamilyCompareService(ILogger<FamilyCompareService> logger)
    {
        _logger = logger;
    }

    public FamilyCompareResult CompareFamilies(
        Dictionary<string, Family> sourceFamilies,
        Dictionary<string, Family> destFamilies,
        IndividualCompareResult individualResult)
    {
        _logger.LogInformation("Starting family comparison. Source: {SourceCount}, Destination: {DestCount}",
            sourceFamilies.Count, destFamilies.Count);

        var matchedFamilies = ImmutableList.CreateBuilder<MatchedFamily>();
        var familiesToUpdate = ImmutableList.CreateBuilder<FamilyToUpdate>();
        var familiesToAdd = ImmutableList.CreateBuilder<FamilyToAdd>();
        var familiesToDelete = ImmutableList.CreateBuilder<FamilyToDelete>();

        // Build ID mapping from individual comparison results
        var sourceToDestMap = BuildIdMapping(individualResult);

        // Track which destination families have been matched
        var matchedDestFamilyIds = new HashSet<string>();

        // Process each source family
        foreach (var (sourceFamId, sourceFamily) in sourceFamilies)
        {
            var matchedDestFamily = FindMatchingFamily(sourceFamily, destFamilies, sourceToDestMap);

            if (matchedDestFamily == null)
            {
                // No matching family found - add to ToAdd list
                familiesToAdd.Add(CreateFamilyToAdd(sourceFamily, sourceToDestMap));
                _logger.LogDebug("No match found for family {FamId}", sourceFamId);
            }
            else
            {
                matchedDestFamilyIds.Add(matchedDestFamily.FamilyId);

                // Check if family needs updates
                var updates = CompareFamilyDetails(sourceFamily, matchedDestFamily, sourceToDestMap);

                if (updates.HasUpdates)
                {
                    familiesToUpdate.Add(updates.ToUpdate);
                    _logger.LogDebug("Family {SourceId} -> {DestId} needs updates",
                        sourceFamId, matchedDestFamily.FamilyId);
                }
                else
                {
                    matchedFamilies.Add(CreateMatchedFamily(sourceFamily, matchedDestFamily, sourceToDestMap));
                    _logger.LogDebug("Family {SourceId} -> {DestId} matched perfectly",
                        sourceFamId, matchedDestFamily.FamilyId);
                }
            }
        }

        // Find unmatched destination families (candidates for deletion)
        foreach (var (destFamId, destFamily) in destFamilies)
        {
            if (!matchedDestFamilyIds.Contains(destFamId))
            {
                familiesToDelete.Add(new FamilyToDelete
                {
                    DestinationFamId = destFamId,
                    Reason = "No matching family found in source"
                });
            }
        }

        _logger.LogInformation(
            "Family comparison complete. Matched: {Matched}, ToUpdate: {ToUpdate}, ToAdd: {ToAdd}, ToDelete: {ToDelete}",
            matchedFamilies.Count, familiesToUpdate.Count, familiesToAdd.Count, familiesToDelete.Count);

        return new FamilyCompareResult
        {
            MatchedFamilies = matchedFamilies.ToImmutable(),
            FamiliesToUpdate = familiesToUpdate.ToImmutable(),
            FamiliesToAdd = familiesToAdd.ToImmutable(),
            FamiliesToDelete = familiesToDelete.ToImmutable()
        };
    }

    private Dictionary<string, string> BuildIdMapping(IndividualCompareResult individualResult)
    {
        var mapping = new Dictionary<string, string>();

        // Add mappings from matched nodes
        foreach (var matched in individualResult.MatchedNodes)
        {
            mapping[matched.SourceId] = matched.DestinationId;
        }

        // Add mappings from nodes to update
        foreach (var toUpdate in individualResult.NodesToUpdate)
        {
            mapping[toUpdate.SourceId] = toUpdate.DestinationId;
        }

        return mapping;
    }

    private Family? FindMatchingFamily(
        Family sourceFamily,
        Dictionary<string, Family> destFamilies,
        Dictionary<string, string> sourceToDestMap)
    {
        var sourceHusbandId = sourceFamily.HusbandId;
        var sourceWifeId = sourceFamily.WifeId;

        // Try to map source spouse IDs to destination IDs
        var destHusbandId = sourceHusbandId != null && sourceToDestMap.ContainsKey(sourceHusbandId)
            ? sourceToDestMap[sourceHusbandId]
            : null;

        var destWifeId = sourceWifeId != null && sourceToDestMap.ContainsKey(sourceWifeId)
            ? sourceToDestMap[sourceWifeId]
            : null;

        // Family is matched if both spouses are matched
        if (destHusbandId != null && destWifeId != null)
        {
            // Find family in destination with these spouse IDs
            foreach (var destFamily in destFamilies.Values)
            {
                if (destFamily.HusbandId == destHusbandId && destFamily.WifeId == destWifeId)
                {
                    return destFamily;
                }
            }
        }

        return null;
    }

    private (bool HasUpdates, FamilyToUpdate ToUpdate) CompareFamilyDetails(
        Family sourceFamily,
        Family destFamily,
        Dictionary<string, string> sourceToDestMap)
    {
        var missingChildren = new List<string>();
        var hasUpdates = false;

        // Check for missing children
        var sourceChildren = sourceFamily.Children?.ToList() ?? new List<string>();
        var destChildren = destFamily.Children?.ToList() ?? new List<string>();

        foreach (var sourceChildId in sourceChildren)
        {
            if (sourceToDestMap.TryGetValue(sourceChildId, out var destChildId))
            {
                if (!destChildren.Contains(destChildId))
                {
                    missingChildren.Add(destChildId);
                    hasUpdates = true;
                }
            }
        }

        // For now, we don't compare marriage/divorce dates from Family records
        // This can be enhanced later if needed

        var toUpdate = new FamilyToUpdate
        {
            SourceFamId = sourceFamily.FamilyId,
            DestinationFamId = destFamily.FamilyId,
            MissingChildren = missingChildren.ToImmutableList(),
            MarriageDate = null,
            MarriagePlace = null,
            DivorceDate = null
        };

        return (hasUpdates, toUpdate);
    }

    private MatchedFamily CreateMatchedFamily(
        Family sourceFamily,
        Family destFamily,
        Dictionary<string, string> sourceToDestMap)
    {
        var childrenMapping = new Dictionary<string, string>();
        var sourceChildren = sourceFamily.Children?.ToList() ?? new List<string>();

        foreach (var sourceChildId in sourceChildren)
        {
            if (sourceToDestMap.TryGetValue(sourceChildId, out var destChildId))
            {
                childrenMapping[sourceChildId] = destChildId;
            }
        }

        return new MatchedFamily
        {
            SourceFamId = sourceFamily.FamilyId,
            DestinationFamId = destFamily.FamilyId,
            HusbandSourceId = sourceFamily.HusbandId,
            HusbandDestinationId = destFamily.HusbandId,
            WifeSourceId = sourceFamily.WifeId,
            WifeDestinationId = destFamily.WifeId,
            ChildrenMapping = childrenMapping.ToImmutableDictionary()
        };
    }

    private FamilyToAdd CreateFamilyToAdd(Family sourceFamily, Dictionary<string, string> sourceToDestMap)
    {
        var husbandId = sourceFamily.HusbandId != null && sourceToDestMap.ContainsKey(sourceFamily.HusbandId)
            ? sourceToDestMap[sourceFamily.HusbandId]
            : sourceFamily.HusbandId;

        var wifeId = sourceFamily.WifeId != null && sourceToDestMap.ContainsKey(sourceFamily.WifeId)
            ? sourceToDestMap[sourceFamily.WifeId]
            : sourceFamily.WifeId;

        var childrenIds = new List<string>();
        if (sourceFamily.Children != null)
        {
            foreach (var sourceChildId in sourceFamily.Children)
            {
                var childId = sourceToDestMap.ContainsKey(sourceChildId)
                    ? sourceToDestMap[sourceChildId]
                    : sourceChildId;
                childrenIds.Add(childId);
            }
        }

        return new FamilyToAdd
        {
            SourceFamId = sourceFamily.FamilyId,
            HusbandId = husbandId,
            WifeId = wifeId,
            ChildrenIds = childrenIds.ToImmutableList(),
            MarriageDate = null,  // Can be enhanced to extract from Family events
            MarriagePlace = null
        };
    }
}
