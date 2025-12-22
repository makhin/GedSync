using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using IssueType = GedcomGeniSync.Core.Models.Wave.IssueType;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Валидация сопоставлений персон перед добавлением в результат.
/// Проверяет базовую совместимость и семейную консистентность.
/// </summary>
public class WaveMappingValidator
{
    private readonly ILogger<WaveMappingValidator>? _logger;

    public WaveMappingValidator(ILogger<WaveMappingValidator>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Валидировать одно сопоставление перед добавлением.
    /// </summary>
    /// <returns>Результат валидации с найденными проблемами</returns>
    public ValidationResult ValidateMapping(
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();

        if (!sourceTree.PersonsById.TryGetValue(newMapping.SourceId, out var sourcePerson))
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.InvalidSourceId,
                SourceId = newMapping.SourceId,
                Message = $"Source person {newMapping.SourceId} not found in source tree"
            });
            return new ValidationResult { IsValid = false, Issues = issues };
        }

        if (!destTree.PersonsById.TryGetValue(newMapping.DestinationId, out var destPerson))
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.InvalidDestId,
                SourceId = newMapping.SourceId,
                DestId = newMapping.DestinationId,
                Message = $"Destination person {newMapping.DestinationId} not found in destination tree"
            });
            return new ValidationResult { IsValid = false, Issues = issues };
        }

        // ═══════════════════════════════════════════════════════════
        // 1. Проверка пола
        // ═══════════════════════════════════════════════════════════
        if (sourcePerson.Gender != destPerson.Gender &&
            sourcePerson.Gender != Gender.Unknown &&
            destPerson.Gender != Gender.Unknown)
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.GenderMismatch,
                SourceId = newMapping.SourceId,
                DestId = newMapping.DestinationId,
                Message = $"Gender mismatch: {sourcePerson.Gender} vs {destPerson.Gender}"
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Проверка года рождения
        // ═══════════════════════════════════════════════════════════
        ValidateYearDifference(
            sourcePerson.BirthYear,
            destPerson.BirthYear,
            "Birth year",
            IssueType.BirthYearMismatch,
            newMapping,
            issues);

        // ═══════════════════════════════════════════════════════════
        // 3. Проверка года смерти
        // ═══════════════════════════════════════════════════════════
        ValidateYearDifference(
            sourcePerson.DeathYear,
            destPerson.DeathYear,
            "Death year",
            IssueType.DeathYearMismatch,
            newMapping,
            issues);

        // ═══════════════════════════════════════════════════════════
        // 4. Проверка на дубликаты destination
        // ═══════════════════════════════════════════════════════════
        var existingForDest = existingMappings.Values
            .FirstOrDefault(m => m.DestinationId == newMapping.DestinationId);

        if (existingForDest != null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.DuplicateMapping,
                SourceId = newMapping.SourceId,
                DestId = newMapping.DestinationId,
                Message = $"Destination {newMapping.DestinationId} already mapped to source {existingForDest.SourceId}"
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 5. Проверка низкого score
        // ═══════════════════════════════════════════════════════════
        if (newMapping.MatchScore < 40)
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.Medium,
                Type = IssueType.LowMatchScore,
                SourceId = newMapping.SourceId,
                DestId = newMapping.DestinationId,
                Message = $"Low match score: {newMapping.MatchScore}"
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 6. Проверка транзитивной семейной консистентности
        // ═══════════════════════════════════════════════════════════
        issues.AddRange(ValidateTransitiveConsistency(
            existingMappings,
            sourcePerson,
            destPerson,
            sourceTree,
            destTree));

        // Считаем валидным если нет проблем High severity
        var isValid = !issues.Any(i => i.Severity == Severity.High);

        if (!isValid)
        {
            _logger?.LogWarning(
                "Mapping {SourceId} -> {DestId} failed validation with {IssueCount} issues",
                newMapping.SourceId,
                newMapping.DestinationId,
                issues.Count(i => i.Severity == Severity.High));
        }
        else if (issues.Any())
        {
            _logger?.LogDebug(
                "Mapping {SourceId} -> {DestId} has {IssueCount} non-critical issues",
                newMapping.SourceId,
                newMapping.DestinationId,
                issues.Count);
        }

        return new ValidationResult
        {
            IsValid = isValid,
            Issues = issues
        };
    }

    private void ValidateYearDifference(
        int? sourceYear,
        int? destYear,
        string fieldName,
        IssueType issueType,
        PersonMapping mapping,
        List<ValidationIssue> issues)
    {
        if (!sourceYear.HasValue || !destYear.HasValue)
            return;

        var diff = Math.Abs(sourceYear.Value - destYear.Value);
        var (severity, message) = diff switch
        {
            > 15 => (Severity.High, $"{fieldName} differs by {diff} years"),
            > 5 => (Severity.Medium, $"{fieldName} differs by {diff} years"),
            _ => ((Severity?)null, (string?)null)
        };

        if (severity.HasValue)
        {
            issues.Add(new ValidationIssue
            {
                Severity = severity.Value,
                Type = issueType,
                SourceId = mapping.SourceId,
                DestId = mapping.DestinationId,
                Message = $"{message} ({sourceYear} vs {destYear})"
            });
        }
    }

    /// <summary>
    /// Проверить транзитивную консистентность семейных связей.
    /// </summary>
    private List<ValidationIssue> ValidateTransitiveConsistency(
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();

        issues.AddRange(ValidateSpouseConsistency(
            sourcePerson,
            destPerson,
            existingMappings,
            sourceTree,
            destTree));

        issues.AddRange(ValidateParentConsistency(
            sourcePerson,
            destPerson,
            existingMappings,
            sourceTree,
            destTree));

        issues.AddRange(ValidateChildrenConsistency(
            sourcePerson,
            destPerson,
            existingMappings,
            sourceTree,
            destTree));

        issues.AddRange(ValidateSiblingConsistency(
            sourcePerson,
            destPerson,
            existingMappings,
            sourceTree,
            destTree));

        return issues;
    }

    /// <summary>
    /// Проверка консистентности сопоставленных супругов.
    /// </summary>
    private List<ValidationIssue> ValidateSpouseConsistency(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();
        var destSpouses = TreeNavigator.GetSpouses(destTree, destPerson.Id).ToHashSet();

        foreach (var sourceSpouseId in TreeNavigator.GetSpouses(sourceTree, sourcePerson.Id))
        {
            if (!existingMappings.TryGetValue(sourceSpouseId, out var spouseMapping))
                continue;

            var destSpouseId = spouseMapping.DestinationId;
            if (!destSpouses.Contains(destSpouseId))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    SourceId = sourcePerson.Id,
                    DestId = destPerson.Id,
                    RelatedSourceId = sourceSpouseId,
                    RelatedDestinationId = destSpouseId,
                    Message = $"Spouse inconsistency: {sourcePerson.Id}'s spouse {sourceSpouseId} is mapped to {destSpouseId}, but {destPerson.Id} is not married to {destSpouseId} in destination tree"
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Проверка консистентности сопоставленных родителей.
    /// </summary>
    private List<ValidationIssue> ValidateParentConsistency(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();
        var destParents = TreeNavigator.GetParents(destTree, destPerson.Id).ToHashSet();

        foreach (var sourceParentId in TreeNavigator.GetParents(sourceTree, sourcePerson.Id))
        {
            if (!existingMappings.TryGetValue(sourceParentId, out var parentMapping))
                continue;

            var destParentId = parentMapping.DestinationId;
            if (!destParents.Contains(destParentId))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    SourceId = sourcePerson.Id,
                    DestId = destPerson.Id,
                    RelatedSourceId = sourceParentId,
                    RelatedDestinationId = destParentId,
                    Message = $"Parent inconsistency: {sourcePerson.Id}'s parent {sourceParentId} is mapped to {destParentId}, but {destPerson.Id} does not list {destParentId} as a parent in destination tree"
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Проверка консистентности сопоставленных детей.
    /// </summary>
    private List<ValidationIssue> ValidateChildrenConsistency(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();
        var destChildren = TreeNavigator.GetChildren(destTree, destPerson.Id).ToHashSet();

        foreach (var sourceChildId in TreeNavigator.GetChildren(sourceTree, sourcePerson.Id))
        {
            if (!existingMappings.TryGetValue(sourceChildId, out var childMapping))
                continue;

            var destChildId = childMapping.DestinationId;
            if (!destChildren.Contains(destChildId))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    SourceId = sourcePerson.Id,
                    DestId = destPerson.Id,
                    RelatedSourceId = sourceChildId,
                    RelatedDestinationId = destChildId,
                    Message = $"Child inconsistency: {sourcePerson.Id}'s child {sourceChildId} is mapped to {destChildId}, but {destPerson.Id} does not list {destChildId} as a child in destination tree"
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Проверка консистентности сопоставленных сиблингов.
    /// </summary>
    private List<ValidationIssue> ValidateSiblingConsistency(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();
        var destSiblings = TreeNavigator.GetSiblings(destTree, destPerson.Id).ToHashSet();

        foreach (var sourceSiblingId in TreeNavigator.GetSiblings(sourceTree, sourcePerson.Id))
        {
            if (!existingMappings.TryGetValue(sourceSiblingId, out var siblingMapping))
                continue;

            var destSiblingId = siblingMapping.DestinationId;
            if (!destSiblings.Contains(destSiblingId))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    SourceId = sourcePerson.Id,
                    DestId = destPerson.Id,
                    RelatedSourceId = sourceSiblingId,
                    RelatedDestinationId = destSiblingId,
                    Message = $"Sibling inconsistency: {sourcePerson.Id}'s sibling {sourceSiblingId} is mapped to {destSiblingId}, but {destPerson.Id} is not a sibling of {destSiblingId} in destination tree"
                });
            }
        }

        return issues;
    }
}

/// <summary>
/// Результат валидации сопоставления.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
}
