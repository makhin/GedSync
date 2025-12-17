using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing family (FAM) records between source and destination GEDCOM files
/// Matches families based on matched spouse IDs
/// </summary>
public class FamilyCompareService : IFamilyCompareService
{
    private readonly ILogger<FamilyCompareService> _logger;
    private readonly IFuzzyMatcherService _fuzzyMatcher;

    public FamilyCompareService(
        ILogger<FamilyCompareService> logger,
        IFuzzyMatcherService fuzzyMatcher)
    {
        _logger = logger;
        _fuzzyMatcher = fuzzyMatcher;
    }

    public FamilyCompareResult CompareFamilies(
        Dictionary<string, Family> sourceFamilies,
        Dictionary<string, Family> destFamilies,
        IndividualCompareResult individualResult,
        CompareOptions options,
        IReadOnlyDictionary<string, PersonRecord>? sourcePersons = null,
        IReadOnlyDictionary<string, PersonRecord>? destPersons = null)
    {
        _logger.LogInformation("Starting family comparison. Source: {SourceCount}, Destination: {DestCount}",
            sourceFamilies.Count, destFamilies.Count);

        // Set person dictionaries for fuzzy matching
        if (sourcePersons != null && destPersons != null)
        {
            _fuzzyMatcher.SetPersonDictionaries(sourcePersons, destPersons);
            _logger.LogDebug("Person dictionaries set for fuzzy child matching");
        }

        var matchedFamilies = ImmutableList.CreateBuilder<MatchedFamily>();
        var familiesToUpdate = ImmutableList.CreateBuilder<FamilyToUpdate>();
        var familiesToAdd = ImmutableList.CreateBuilder<FamilyToAdd>();
        var familiesToDelete = ImmutableList.CreateBuilder<FamilyToDelete>();
        var newPersonMappings = new Dictionary<string, string>();

        // Build ID mapping from individual comparison results
        var sourceToDestMap = BuildIdMapping(individualResult);

        // Track which destination families have been matched
        var matchedDestFamilyIds = new HashSet<string>();
        var destFamilySignatures = BuildDestinationSignatures(destFamilies);

        // Prioritize families by number of mapped members for better matching accuracy
        var prioritizedFamilies = PrioritizeFamilies(sourceFamilies, sourceToDestMap);

        _logger.LogDebug("Processing {Count} families in priority order (by mapped member count)", sourceFamilies.Count);

        // Process each source family in priority order
        foreach (var (sourceFamId, sourceFamily) in prioritizedFamilies)
        {
            var matchedDestFamily = FindMatchingFamily(
                sourceFamily,
                destFamilies,
                destFamilySignatures,
                sourceToDestMap,
                matchedDestFamilyIds);

            if (matchedDestFamily == null)
            {
                // No matching family found - add to ToAdd list
                familiesToAdd.Add(CreateFamilyToAdd(sourceFamily, sourceToDestMap));
                _logger.LogDebug("No match found for family {FamId}", sourceFamId);
            }
            else
            {
                matchedDestFamilyIds.Add(matchedDestFamily.FamilyId);

                CaptureNewPersonMappings(sourceFamily, matchedDestFamily, sourceToDestMap, newPersonMappings, sourcePersons, destPersons);

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
        if (options.IncludeDeleteSuggestions)
        {
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
        }

        _logger.LogInformation(
            "Family comparison complete. Matched: {Matched}, ToUpdate: {ToUpdate}, ToAdd: {ToAdd}, ToDelete: {ToDelete}",
            matchedFamilies.Count, familiesToUpdate.Count, familiesToAdd.Count, familiesToDelete.Count);

        return new FamilyCompareResult
        {
            MatchedFamilies = matchedFamilies.ToImmutable(),
            FamiliesToUpdate = familiesToUpdate.ToImmutable(),
            FamiliesToAdd = familiesToAdd.ToImmutable(),
            FamiliesToDelete = familiesToDelete.ToImmutable(),
            NewPersonMappings = newPersonMappings.ToImmutableDictionary()
        };
    }

    private Dictionary<string, string> TryMatchUnmappedChildren(
        List<string> unmappedSourceChildren,
        List<string> unmappedDestChildren,
        IReadOnlyDictionary<string, PersonRecord>? sourcePersons,
        IReadOnlyDictionary<string, PersonRecord>? destPersons)
    {
        var mappings = new Dictionary<string, string>();

        // If person records are not provided, cannot perform fuzzy matching
        if (sourcePersons == null || destPersons == null)
        {
            return mappings;
        }

        // Determine threshold based on child counts
        // Equal counts: use standard threshold (70)
        // Unequal counts: use very high threshold (85) to avoid incorrect mappings
        var threshold = unmappedSourceChildren.Count == unmappedDestChildren.Count ? 70 : 85;

        // Build candidate matrix with fuzzy scores
        var candidates = new List<ChildMatchCandidate>();

        foreach (var sourceChildId in unmappedSourceChildren)
        {
            if (!sourcePersons.TryGetValue(sourceChildId, out var sourcePerson))
            {
                continue;
            }

            foreach (var destChildId in unmappedDestChildren)
            {
                if (!destPersons.TryGetValue(destChildId, out var destPerson))
                {
                    continue;
                }

                var matchResult = _fuzzyMatcher.Compare(sourcePerson, destPerson);

                if (matchResult.Score >= threshold)
                {
                    candidates.Add(new ChildMatchCandidate
                    {
                        SourceId = sourceChildId,
                        DestId = destChildId,
                        Score = matchResult.Score
                    });
                }
            }
        }

        // Greedy algorithm: select best pairs without conflicts
        var usedSourceIds = new HashSet<string>();
        var usedDestIds = new HashSet<string>();

        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (usedSourceIds.Contains(candidate.SourceId) || usedDestIds.Contains(candidate.DestId))
            {
                continue;
            }

            mappings[candidate.SourceId] = candidate.DestId;
            usedSourceIds.Add(candidate.SourceId);
            usedDestIds.Add(candidate.DestId);

            _logger.LogDebug(
                "Fuzzy matched children: {SourceId} -> {DestId} (score: {Score})",
                candidate.SourceId, candidate.DestId, candidate.Score);
        }

        if (mappings.Count > 0)
        {
            _logger.LogInformation(
                "Fuzzy matched {Count} children pairs using threshold {Threshold}",
                mappings.Count, threshold);
        }

        return mappings;
    }

    private record ChildMatchCandidate
    {
        public required string SourceId { get; init; }
        public required string DestId { get; init; }
        public double Score { get; init; }
    }

    private void CaptureNewPersonMappings(
        Family sourceFamily,
        Family destFamily,
        Dictionary<string, string> sourceToDestMap,
        Dictionary<string, string> newPersonMappings,
        IReadOnlyDictionary<string, PersonRecord>? sourcePersons,
        IReadOnlyDictionary<string, PersonRecord>? destPersons)
    {
        void TryAddMapping(string? sourceId, string? destId)
        {
            if (sourceId == null || destId == null)
            {
                return;
            }

            if (sourceToDestMap.ContainsKey(sourceId)
                || newPersonMappings.ContainsKey(sourceId)
                || sourceToDestMap.ContainsValue(destId)
                || newPersonMappings.ContainsValue(destId))
            {
                return;
            }

            newPersonMappings[sourceId] = destId;
        }

        TryAddMapping(sourceFamily.HusbandId, destFamily.HusbandId);
        TryAddMapping(sourceFamily.WifeId, destFamily.WifeId);

        var sourceChildren = sourceFamily.Children?.ToList() ?? new List<string>();
        var destChildren = destFamily.Children?.ToList() ?? new List<string>();

        var mappedDestinationIds = new HashSet<string>(sourceToDestMap.Values.Concat(newPersonMappings.Values));
        var unmappedSourceChildren = sourceChildren
            .Where(id => !sourceToDestMap.ContainsKey(id) && !newPersonMappings.ContainsKey(id))
            .ToList();
        var unmappedDestChildren = destChildren
            .Where(id => !mappedDestinationIds.Contains(id))
            .ToList();

        // Try fuzzy matching for unmapped children
        if (unmappedSourceChildren.Count > 0 && unmappedDestChildren.Count > 0)
        {
            var childMappings = TryMatchUnmappedChildren(
                unmappedSourceChildren,
                unmappedDestChildren,
                sourcePersons,
                destPersons);

            foreach (var (sourceChildId, destChildId) in childMappings)
            {
                TryAddMapping(sourceChildId, destChildId);
            }
        }
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
        Dictionary<string, FamilyMemberSignature> destFamilySignatures,
        Dictionary<string, string> sourceToDestMap,
        HashSet<string> matchedDestFamilyIds)
    {
        var sourceChildren = sourceFamily.Children?.ToList() ?? new List<string>();
        var candidateSignature = BuildCandidateSignature(sourceFamily, sourceToDestMap, sourceChildren);
        var allChildrenMapped = sourceChildren.Count == candidateSignature.Children.Count;

        var matchedFamily = MatchBySignature(
            candidateSignature,
            destFamilies,
            destFamilySignatures,
            matchedDestFamilyIds,
            allowUnmappedSpouses: false);

        if (matchedFamily == null && allChildrenMapped && candidateSignature.Children.Count > 0)
        {
            matchedFamily = MatchBySignature(
                candidateSignature,
                destFamilies,
                destFamilySignatures,
                matchedDestFamilyIds,
                allowUnmappedSpouses: true);
        }

        return matchedFamily;
    }

    private Family? MatchBySignature(
        FamilyMemberSignature candidate,
        Dictionary<string, Family> destFamilies,
        Dictionary<string, FamilyMemberSignature> destFamilySignatures,
        HashSet<string> matchedDestFamilyIds,
        bool allowUnmappedSpouses)
    {
        foreach (var (destId, signature) in destFamilySignatures)
        {
            if (matchedDestFamilyIds.Contains(destId))
            {
                continue;
            }

            if (SignatureMatches(candidate, signature, allowUnmappedSpouses))
            {
                return destFamilies[destId];
            }
        }

        return null;
    }

    private bool SignatureMatches(
        FamilyMemberSignature candidate,
        FamilyMemberSignature destination,
        bool allowUnmappedSpouses)
    {
        if (candidate.HusbandId != null && destination.HusbandId != candidate.HusbandId)
        {
            return false;
        }

        if (candidate.WifeId != null && destination.WifeId != candidate.WifeId)
        {
            return false;
        }

        if (!allowUnmappedSpouses)
        {
            if (candidate.HusbandId == null && destination.HusbandId != null)
            {
                return false;
            }

            if (candidate.WifeId == null && destination.WifeId != null)
            {
                return false;
            }
        }

        return candidate.Children.IsSubsetOf(destination.Children);
    }

    private FamilyMemberSignature BuildCandidateSignature(
        Family sourceFamily,
        Dictionary<string, string> sourceToDestMap,
        IReadOnlyCollection<string> sourceChildren)
    {
        var mappedChildren = sourceChildren
            .Select(id => sourceToDestMap.TryGetValue(id, out var mappedId) ? mappedId : null)
            .Where(id => id != null)
            .Select(id => id!)
            .ToImmutableHashSet();

        var destHusbandId = sourceFamily.HusbandId != null && sourceToDestMap.ContainsKey(sourceFamily.HusbandId)
            ? sourceToDestMap[sourceFamily.HusbandId]
            : null;

        var destWifeId = sourceFamily.WifeId != null && sourceToDestMap.ContainsKey(sourceFamily.WifeId)
            ? sourceToDestMap[sourceFamily.WifeId]
            : null;

        return new FamilyMemberSignature(destHusbandId, destWifeId, mappedChildren);
    }

    private Dictionary<string, FamilyMemberSignature> BuildDestinationSignatures(
        Dictionary<string, Family> destFamilies)
    {
        return destFamilies.ToDictionary(
            pair => pair.Key,
            pair => new FamilyMemberSignature(
                pair.Value.HusbandId,
                pair.Value.WifeId,
                pair.Value.Children?.ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty));
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

        var destChildren = destFamily.Children?.ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty;

        foreach (var sourceChildId in sourceChildren)
        {
            if (sourceToDestMap.TryGetValue(sourceChildId, out var destChildId)
                && destChildren.Contains(destChildId))
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

    private record FamilyMemberSignature(string? HusbandId, string? WifeId, ImmutableHashSet<string> Children);

    private IEnumerable<KeyValuePair<string, Family>> PrioritizeFamilies(
        Dictionary<string, Family> families,
        Dictionary<string, string> existingMappings)
    {
        return families
            .Select(kvp => new
            {
                Pair = kvp,
                MappedCount = CountMappedMembers(kvp.Value, existingMappings),
                TotalCount = GetTotalMemberCount(kvp.Value),
                Confidence = CalculateFamilyConfidence(kvp.Value, existingMappings)
            })
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.MappedCount)
            .Select(x => x.Pair);
    }

    private double CalculateFamilyConfidence(Family family, Dictionary<string, string> mappings)
    {
        var total = GetTotalMemberCount(family);
        var mapped = CountMappedMembers(family, mappings);

        if (total == 0) return 0;

        var ratio = (double)mapped / total;

        // Bonus if both spouses are mapped
        var bothSpousesMapped =
            (family.HusbandId == null || mappings.ContainsKey(family.HusbandId)) &&
            (family.WifeId == null || mappings.ContainsKey(family.WifeId));

        if (bothSpousesMapped)
            ratio += 0.2;

        return Math.Min(ratio, 1.0);
    }

    private int CountMappedMembers(Family family, Dictionary<string, string> mappings)
    {
        var count = 0;

        if (family.HusbandId != null && mappings.ContainsKey(family.HusbandId))
            count++;

        if (family.WifeId != null && mappings.ContainsKey(family.WifeId))
            count++;

        if (family.Children != null)
        {
            count += family.Children.Count(childId => mappings.ContainsKey(childId));
        }

        return count;
    }

    private int GetTotalMemberCount(Family family)
    {
        var count = 0;

        if (family.HusbandId != null) count++;
        if (family.WifeId != null) count++;
        if (family.Children != null) count += family.Children.Count();

        return count;
    }
}
