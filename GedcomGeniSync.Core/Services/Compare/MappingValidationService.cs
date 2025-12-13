using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for validating person mappings and detecting issues
/// </summary>
public class MappingValidationService : IMappingValidationService
{
    private readonly ILogger<MappingValidationService> _logger;

    public MappingValidationService(ILogger<MappingValidationService> logger)
    {
        _logger = logger;
    }

    public ValidationResult ValidateMappings(
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons,
        IReadOnlyDictionary<string, Family> sourceFamilies,
        IReadOnlyDictionary<string, Family> destFamilies)
    {
        var issues = ImmutableList.CreateBuilder<MappingIssue>();

        // Check for duplicate mappings (multiple sources -> same dest)
        var destCounts = new Dictionary<string, List<string>>();
        foreach (var (sourceId, destId) in mappings)
        {
            if (!destCounts.ContainsKey(destId))
            {
                destCounts[destId] = new List<string>();
            }
            destCounts[destId].Add(sourceId);
        }

        foreach (var (destId, sourceIds) in destCounts.Where(kvp => kvp.Value.Count > 1))
        {
            foreach (var sourceId in sourceIds)
            {
                issues.Add(new MappingIssue
                {
                    SourceId = sourceId,
                    DestId = destId,
                    Type = IssueType.DuplicateMapping,
                    Severity = IssueSeverity.High,
                    Description = $"Multiple sources ({string.Join(", ", sourceIds)}) mapped to same destination {destId}"
                });
            }
        }

        // Validate each mapping
        foreach (var (sourceId, destId) in mappings)
        {
            if (!sourcePersons.TryGetValue(sourceId, out var sourcePerson) ||
                !destPersons.TryGetValue(destId, out var destPerson))
            {
                continue;
            }

            // Check 1: Gender mismatch
            if (sourcePerson.Gender != Gender.Unknown && destPerson.Gender != Gender.Unknown &&
                sourcePerson.Gender != destPerson.Gender)
            {
                issues.Add(new MappingIssue
                {
                    SourceId = sourceId,
                    DestId = destId,
                    Type = IssueType.GenderMismatch,
                    Severity = IssueSeverity.High,
                    Description = $"Gender mismatch: {sourcePerson.Gender} vs {destPerson.Gender}"
                });
            }

            // Check 2: Date contradictions
            if (HasDateContradiction(sourcePerson, destPerson, out var dateDesc))
            {
                issues.Add(new MappingIssue
                {
                    SourceId = sourceId,
                    DestId = destId,
                    Type = IssueType.DateContradiction,
                    Severity = IssueSeverity.Medium,
                    Description = dateDesc
                });
            }

            // Check 3: Family role consistency
            var roleIssues = ValidateFamilyRoles(sourceId, destId, mappings, sourceFamilies, destFamilies);
            issues.AddRange(roleIssues);
        }

        var result = new ValidationResult { Issues = issues.ToImmutable() };

        _logger.LogInformation(
            "Validation completed. Total issues: {Total}, High: {High}, Medium: {Medium}, Low: {Low}",
            result.Issues.Count,
            result.HighSeverityCount,
            result.MediumSeverityCount,
            result.LowSeverityCount);

        return result;
    }

    public Dictionary<string, string> RollbackSuspiciousMappings(
        Dictionary<string, string> mappings,
        ValidationResult validation,
        IReadOnlyDictionary<string, Family> sourceFamilies)
    {
        var toRemove = new HashSet<string>();

        // Remove mappings with high severity issues
        foreach (var issue in validation.Issues.Where(i => i.Severity == IssueSeverity.High))
        {
            toRemove.Add(issue.SourceId);

            // Also remove dependent mappings (family members)
            var dependentIds = FindDependentMappings(issue.SourceId, mappings, sourceFamilies);
            foreach (var depId in dependentIds)
            {
                toRemove.Add(depId);
            }
        }

        if (toRemove.Count > 0)
        {
            _logger.LogWarning(
                "Rolling back {Count} suspicious mappings: {Ids}",
                toRemove.Count,
                string.Join(", ", toRemove));
        }

        return mappings
            .Where(kvp => !toRemove.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public double CalculateConfidence(double score, string matchedBy)
    {
        return matchedBy switch
        {
            "RFN" => 1.0,                           // 100% confidence
            "ExistingMapping" => 1.0,                // 100% confidence (anchor)
            "Fuzzy" => Math.Min(score / 100.0, 1.0), // Proportional to score
            "Family_SingleChild" => 0.85,            // High but not absolute
            "Family_FuzzyChild" => 0.70,             // Medium
            "Sibling_Transitive" => 0.65,            // Medium
            _ => 0.50                                 // Default low confidence
        };
    }

    private bool HasDateContradiction(PersonRecord source, PersonRecord dest, out string description)
    {
        description = string.Empty;

        // Check birth date difference
        if (source.BirthYear.HasValue && dest.BirthYear.HasValue)
        {
            var yearDiff = Math.Abs(source.BirthYear.Value - dest.BirthYear.Value);
            if (yearDiff > 5)
            {
                description = $"Birth year difference too large: {yearDiff} years ({source.BirthYear} vs {dest.BirthYear})";
                return true;
            }
        }

        // Check death before birth
        if (source.DeathYear.HasValue && dest.BirthYear.HasValue &&
            source.DeathYear.Value < dest.BirthYear.Value)
        {
            description = $"Death year before birth year: death={source.DeathYear}, birth={dest.BirthYear}";
            return true;
        }

        if (dest.DeathYear.HasValue && source.BirthYear.HasValue &&
            dest.DeathYear.Value < source.BirthYear.Value)
        {
            description = $"Death year before birth year: death={dest.DeathYear}, birth={source.BirthYear}";
            return true;
        }

        return false;
    }

    private IEnumerable<MappingIssue> ValidateFamilyRoles(
        string sourceId,
        string destId,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, Family> sourceFamilies,
        IReadOnlyDictionary<string, Family> destFamilies)
    {
        var issues = new List<MappingIssue>();

        // Find families where person appears as parent
        var sourceParentFamilies = sourceFamilies.Values
            .Where(f => f.HusbandId == sourceId || f.WifeId == sourceId)
            .ToList();

        var destParentFamilies = destFamilies.Values
            .Where(f => f.HusbandId == destId || f.WifeId == destId)
            .ToList();

        // Find families where person appears as child
        var sourceChildFamilies = sourceFamilies.Values
            .Where(f => f.Children != null && f.Children.Contains(sourceId))
            .ToList();

        var destChildFamilies = destFamilies.Values
            .Where(f => f.Children != null && f.Children.Contains(destId))
            .ToList();

        // Check for generational inconsistency
        // If person is parent in source but child in dest (or vice versa)
        if (sourceParentFamilies.Any() && destChildFamilies.Any() && !destParentFamilies.Any())
        {
            issues.Add(new MappingIssue
            {
                SourceId = sourceId,
                DestId = destId,
                Type = IssueType.GenerationalInconsistency,
                Severity = IssueSeverity.High,
                Description = "Person is parent in source but only child in destination"
            });
        }

        if (sourceChildFamilies.Any() && destParentFamilies.Any() && !destChildFamilies.Any())
        {
            issues.Add(new MappingIssue
            {
                SourceId = sourceId,
                DestId = destId,
                Type = IssueType.GenerationalInconsistency,
                Severity = IssueSeverity.High,
                Description = "Person is child in source but only parent in destination"
            });
        }

        return issues;
    }

    private HashSet<string> FindDependentMappings(
        string personId,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, Family> sourceFamilies)
    {
        var dependents = new HashSet<string>();

        // Find all family members (spouse and children)
        foreach (var family in sourceFamilies.Values)
        {
            if (family.HusbandId == personId || family.WifeId == personId)
            {
                // Add spouse
                if (family.HusbandId != null && family.HusbandId != personId && mappings.ContainsKey(family.HusbandId))
                {
                    dependents.Add(family.HusbandId);
                }
                if (family.WifeId != null && family.WifeId != personId && mappings.ContainsKey(family.WifeId))
                {
                    dependents.Add(family.WifeId);
                }

                // Add children
                if (family.Children != null)
                {
                    foreach (var childId in family.Children)
                    {
                        if (mappings.ContainsKey(childId))
                        {
                            dependents.Add(childId);
                        }
                    }
                }
            }
        }

        return dependents;
    }
}
