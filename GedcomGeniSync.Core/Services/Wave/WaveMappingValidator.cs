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
        // 6. Проверка семейной консистентности
        // ═══════════════════════════════════════════════════════════
        ValidateFamilyConsistency(
            newMapping,
            existingMappings,
            sourcePerson,
            destPerson,
            sourceTree,
            destTree,
            issues);

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
    /// Проверить консистентность семейных связей.
    /// </summary>
    private void ValidateFamilyConsistency(
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        TreeGraph sourceTree,
        TreeGraph destTree,
        List<ValidationIssue> issues)
    {
        // Проверяем родителей
        ValidateParent(
            sourcePerson.FatherId,
            destPerson.FatherId,
            "Father",
            newMapping,
            existingMappings,
            issues);

        ValidateParent(
            sourcePerson.MotherId,
            destPerson.MotherId,
            "Mother",
            newMapping,
            existingMappings,
            issues);

        // Проверяем супругов (если сопоставлены)
        ValidateSpouses(
            sourcePerson,
            destPerson,
            newMapping,
            existingMappings,
            sourceTree,
            destTree,
            issues);

        // Проверяем детей (если сопоставлены)
        ValidateChildren(
            sourcePerson,
            destPerson,
            newMapping,
            existingMappings,
            sourceTree,
            destTree,
            issues);
    }

    private void ValidateParent(
        string? sourceParentId,
        string? destParentId,
        string parentType,
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        List<ValidationIssue> issues)
    {
        if (sourceParentId != null && existingMappings.TryGetValue(sourceParentId, out var parentMapping))
        {
            // Родитель сопоставлен — проверяем совпадение
            if (destParentId != parentMapping.DestinationId)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    SourceId = newMapping.SourceId,
                    DestId = newMapping.DestinationId,
                    Message = $"{parentType} mismatch: expected {parentMapping.DestinationId}, got {destParentId}"
                });
            }
        }
    }

    private void ValidateSpouses(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree,
        List<ValidationIssue> issues)
    {
        // Получаем супругов из семей
        var sourceSpouses = TreeNavigator.GetSpouses(sourceTree, sourcePerson.Id).ToHashSet();
        var destSpouses = TreeNavigator.GetSpouses(destTree, destPerson.Id).ToHashSet();

        // Проверяем сопоставленных супругов
        foreach (var sourceSpouseId in sourceSpouses)
        {
            if (existingMappings.TryGetValue(sourceSpouseId, out var spouseMapping))
            {
                if (!destSpouses.Contains(spouseMapping.DestinationId))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Medium,
                        Type = IssueType.FamilyInconsistency,
                        SourceId = newMapping.SourceId,
                        DestId = newMapping.DestinationId,
                        Message = $"Spouse {sourceSpouseId} mapped to {spouseMapping.DestinationId} but not spouse in destination"
                    });
                }
            }
        }
    }

    private void ValidateChildren(
        PersonRecord sourcePerson,
        PersonRecord destPerson,
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree,
        List<ValidationIssue> issues)
    {
        // Получаем детей из семей
        var sourceChildren = TreeNavigator.GetChildren(sourceTree, sourcePerson.Id).ToHashSet();
        var destChildren = TreeNavigator.GetChildren(destTree, destPerson.Id).ToHashSet();

        // Проверяем сопоставленных детей
        foreach (var sourceChildId in sourceChildren)
        {
            if (existingMappings.TryGetValue(sourceChildId, out var childMapping))
            {
                if (!destChildren.Contains(childMapping.DestinationId))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Medium,
                        Type = IssueType.FamilyInconsistency,
                        SourceId = newMapping.SourceId,
                        DestId = newMapping.DestinationId,
                        Message = $"Child {sourceChildId} mapped to {childMapping.DestinationId} but not child in destination"
                    });
                }
            }
        }
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
