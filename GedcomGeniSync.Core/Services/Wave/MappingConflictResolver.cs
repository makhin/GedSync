using GedcomGeniSync.Core.Models;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Resolve mapping conflicts using greedy assignment by exclusivity.
/// </summary>
public class MappingConflictResolver
{
    private const int MinimumCandidateScore = 50;
    private readonly IFuzzyMatcherService _fuzzyMatcher;
    private readonly ILogger? _logger;

    public MappingConflictResolver(
        IFuzzyMatcherService fuzzyMatcher,
        ILogger? logger = null)
    {
        _fuzzyMatcher = fuzzyMatcher;
        _logger = logger;
    }

    /// <summary>
    /// Resolve conflicts using greedy algorithm by exclusivity.
    /// </summary>
    public void ResolveConflicts(
        Dictionary<string, PersonMapping> mappings,
        GedcomLoadResult sourceLoadResult,
        GedcomLoadResult destLoadResult)
    {
        var lockedMappings = mappings.Values
            .Where(m => m.FoundVia == GedcomGeniSync.Core.Models.Wave.RelationType.Anchor)
            .ToList();
        var lockedSourceIds = lockedMappings.Select(m => m.SourceId).ToHashSet();
        var lockedDestIds = lockedMappings.Select(m => m.DestinationId).ToHashSet();

        var candidateMatrix = BuildCandidateMatrix(
            mappings,
            sourceLoadResult,
            destLoadResult,
            lockedSourceIds);

        var exclusivityScores = CalculateExclusivity(candidateMatrix);

        var resolvedMappings = GreedyAssignmentByExclusivity(
            candidateMatrix,
            exclusivityScores,
            lockedDestIds);

        UpdateMappings(mappings, resolvedMappings, lockedSourceIds);
    }

    /// <summary>
    /// Build matrix of all possible candidates for each source person.
    /// NEW: Only searches among close relatives of current mapping to avoid global search.
    /// </summary>
    private Dictionary<string, List<CandidateOption>> BuildCandidateMatrix(
        Dictionary<string, PersonMapping> currentMappings,
        GedcomLoadResult sourceLoadResult,
        GedcomLoadResult destLoadResult,
        HashSet<string> lockedSourceIds)
    {
        var matrix = new Dictionary<string, List<CandidateOption>>();

        foreach (var (sourceId, currentMapping) in currentMappings)
        {
            if (lockedSourceIds.Contains(sourceId))
                continue;

            if (!sourceLoadResult.Persons.TryGetValue(sourceId, out var sourcePerson))
            {
                _logger?.LogWarning(
                    "Skipping conflict resolution for {SourceId}: source person not found",
                    sourceId);
                continue;
            }

            // Get close relatives of current mapping destination instead of searching globally
            var relativeCandidates = GetRelativeCandidates(
                currentMapping.DestinationId,
                destLoadResult);

            _logger?.LogDebug(
                "Conflict resolution for {SourceId}: searching among {Count} relatives of {DestId}",
                sourceId,
                relativeCandidates.Count,
                currentMapping.DestinationId);

            var candidates = _fuzzyMatcher.FindMatches(
                sourcePerson,
                relativeCandidates,
                minScore: MinimumCandidateScore);

            var options = candidates
                .Select(mc => new CandidateOption
                {
                    SourceId = sourceId,
                    DestinationId = mc.Target.Id,
                    Score = (int)Math.Round(mc.Score)
                })
                .ToList();

            if (options.All(c => c.DestinationId != currentMapping.DestinationId))
            {
                options.Add(new CandidateOption
                {
                    SourceId = sourceId,
                    DestinationId = currentMapping.DestinationId,
                    Score = currentMapping.MatchScore
                });
            }

            matrix[sourceId] = options;
        }

        return matrix;
    }

    /// <summary>
    /// Calculate exclusivity: how unique is this dest person for this source person.
    /// </summary>
    private Dictionary<string, Dictionary<string, double>> CalculateExclusivity(
        Dictionary<string, List<CandidateOption>> candidateMatrix)
    {
        var exclusivity = new Dictionary<string, Dictionary<string, double>>();

        foreach (var (sourceId, candidates) in candidateMatrix)
        {
            exclusivity[sourceId] = new Dictionary<string, double>();

            if (candidates.Count == 0)
                continue;

            var sorted = candidates.OrderByDescending(c => c.Score).ToList();
            var bestScore = sorted[0].Score;
            var secondBestScore = sorted.Count > 1 ? sorted[1].Score : 0;

            foreach (var candidate in candidates)
            {
                var gap = candidate.Score - secondBestScore;
                var exclusivityScore = bestScore > 0
                    ? (double)gap / bestScore
                    : 0;

                exclusivity[sourceId][candidate.DestinationId] = exclusivityScore;
            }
        }

        return exclusivity;
    }

    /// <summary>
    /// Greedy assignment: prioritize by exclusivity, then by score.
    /// </summary>
    private Dictionary<string, CandidateOption> GreedyAssignmentByExclusivity(
        Dictionary<string, List<CandidateOption>> candidateMatrix,
        Dictionary<string, Dictionary<string, double>> exclusivity,
        HashSet<string> lockedDestIds)
    {
        var assigned = new Dictionary<string, CandidateOption>();
        var usedDestIds = new HashSet<string>(lockedDestIds);
        var queue = new List<AssignmentCandidate>();

        foreach (var (sourceId, candidates) in candidateMatrix)
        {
            foreach (var candidate in candidates)
            {
                var excl = exclusivity[sourceId][candidate.DestinationId];
                queue.Add(new AssignmentCandidate
                {
                    SourceId = sourceId,
                    DestinationId = candidate.DestinationId,
                    Score = candidate.Score,
                    Exclusivity = excl
                });
            }
        }

        var sorted = queue
            .OrderByDescending(a => a.Exclusivity)
            .ThenByDescending(a => a.Score)
            .ToList();

        foreach (var assignment in sorted)
        {
            if (assigned.ContainsKey(assignment.SourceId))
                continue;

            if (usedDestIds.Contains(assignment.DestinationId))
                continue;

            assigned[assignment.SourceId] = new CandidateOption
            {
                SourceId = assignment.SourceId,
                DestinationId = assignment.DestinationId,
                Score = assignment.Score
            };
            usedDestIds.Add(assignment.DestinationId);

            _logger?.LogDebug(
                "Assigned {SourceId} → {DestId} (score: {Score}, exclusivity: {Excl:F2})",
                assignment.SourceId,
                assignment.DestinationId,
                assignment.Score,
                assignment.Exclusivity);
        }

        return assigned;
    }

    private void UpdateMappings(
        Dictionary<string, PersonMapping> originalMappings,
        Dictionary<string, CandidateOption> resolvedMappings,
        HashSet<string> lockedSourceIds)
    {
        foreach (var (sourceId, resolved) in resolvedMappings)
        {
            if (lockedSourceIds.Contains(sourceId))
                continue;

            if (!originalMappings.TryGetValue(sourceId, out var oldMapping))
                continue;

            if (oldMapping.DestinationId == resolved.DestinationId &&
                oldMapping.MatchScore == resolved.Score)
                continue;

            originalMappings[sourceId] = oldMapping with
            {
                DestinationId = resolved.DestinationId,
                MatchScore = resolved.Score
            };

            _logger?.LogInformation(
                "Conflict resolved: {SourceId} changed from {OldDest} to {NewDest}",
                sourceId,
                oldMapping.DestinationId,
                resolved.DestinationId);
        }
    }

    /// <summary>
    /// Get close relatives of a person as candidates for conflict resolution.
    /// Returns family members within 2 degrees of relationship.
    /// </summary>
    private List<PersonRecord> GetRelativeCandidates(
        string destId,
        GedcomLoadResult destLoadResult)
    {
        if (!destLoadResult.Persons.TryGetValue(destId, out var destPerson))
        {
            _logger?.LogWarning(
                "Cannot find dest person {DestId} for conflict resolution, using empty candidate list",
                destId);
            return new List<PersonRecord>();
        }

        var candidates = new HashSet<PersonRecord>();
        var candidateIds = new HashSet<string>();

        // Add the person themselves
        candidateIds.Add(destId);

        // Add parents
        if (!string.IsNullOrEmpty(destPerson.FatherId))
            candidateIds.Add(destPerson.FatherId);
        if (!string.IsNullOrEmpty(destPerson.MotherId))
            candidateIds.Add(destPerson.MotherId);

        // Add spouses
        foreach (var spouseId in destPerson.SpouseIds)
            candidateIds.Add(spouseId);

        // Add children
        foreach (var childId in destPerson.ChildrenIds)
            candidateIds.Add(childId);

        // Add siblings
        foreach (var siblingId in destPerson.SiblingIds)
            candidateIds.Add(siblingId);

        // Add grandparents (parents of parents)
        foreach (var parentId in new[] { destPerson.FatherId, destPerson.MotherId })
        {
            if (string.IsNullOrEmpty(parentId))
                continue;

            if (destLoadResult.Persons.TryGetValue(parentId, out var parent))
            {
                if (!string.IsNullOrEmpty(parent.FatherId))
                    candidateIds.Add(parent.FatherId);
                if (!string.IsNullOrEmpty(parent.MotherId))
                    candidateIds.Add(parent.MotherId);

                // Add parent's spouses (step-parents)
                foreach (var spouseId in parent.SpouseIds)
                    candidateIds.Add(spouseId);
            }
        }

        // Add grandchildren (children of children)
        foreach (var childId in destPerson.ChildrenIds)
        {
            if (destLoadResult.Persons.TryGetValue(childId, out var child))
            {
                foreach (var grandchildId in child.ChildrenIds)
                    candidateIds.Add(grandchildId);

                // Add child's spouses
                foreach (var spouseId in child.SpouseIds)
                    candidateIds.Add(spouseId);
            }
        }

        // Add nieces/nephews (children of siblings)
        foreach (var siblingId in destPerson.SiblingIds)
        {
            if (destLoadResult.Persons.TryGetValue(siblingId, out var sibling))
            {
                foreach (var nieceNephewId in sibling.ChildrenIds)
                    candidateIds.Add(nieceNephewId);
            }
        }

        // Add aunts/uncles (siblings of parents)
        foreach (var parentId in new[] { destPerson.FatherId, destPerson.MotherId })
        {
            if (string.IsNullOrEmpty(parentId))
                continue;

            if (destLoadResult.Persons.TryGetValue(parentId, out var parent))
            {
                foreach (var auntUncleId in parent.SiblingIds)
                    candidateIds.Add(auntUncleId);
            }
        }

        // Resolve IDs to PersonRecords
        foreach (var id in candidateIds)
        {
            if (destLoadResult.Persons.TryGetValue(id, out var person))
            {
                candidates.Add(person);
            }
        }

        _logger?.LogDebug(
            "Found {Count} relative candidates for conflict resolution of {DestId} ({Name})",
            candidates.Count,
            destId,
            destPerson.FullName);

        return candidates.ToList();
    }
}

internal record CandidateOption
{
    public required string SourceId { get; init; }
    public required string DestinationId { get; init; }
    public required int Score { get; init; }
}

internal record AssignmentCandidate
{
    public required string SourceId { get; init; }
    public required string DestinationId { get; init; }
    public required int Score { get; init; }
    public required double Exclusivity { get; init; }
}
