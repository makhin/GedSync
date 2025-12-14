using System.Text;
using GedcomGeniSync.Core.Models.Wave;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Форматирует детализированный лог wave сравнения в читаемый текстовый формат.
/// </summary>
public class WaveCompareLogFormatter
{
    /// <summary>
    /// Форматировать весь лог в текстовый формат.
    /// </summary>
    public string FormatLog(WaveCompareLog log)
    {
        var sb = new StringBuilder();

        // Заголовок
        sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                    WAVE COMPARE DETAILED LOG                                 ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        // Общая информация
        sb.AppendLine($"Source File:      {log.SourceFile}");
        sb.AppendLine($"Destination File: {log.DestinationFile}");
        sb.AppendLine($"Anchor Source:    {log.AnchorSourceId}");
        sb.AppendLine($"Anchor Dest:      {log.AnchorDestId}");
        sb.AppendLine($"Max Level:        {log.MaxLevel}");
        sb.AppendLine($"Strategy:         {log.Strategy}");
        sb.AppendLine($"Duration:         {(log.EndTime - log.StartTime).TotalSeconds:F2}s");
        sb.AppendLine();

        // Результаты
        sb.AppendLine($"Total Mappings:   {log.Result.Statistics.TotalMappings}/{log.Result.Statistics.TotalSourcePersons}");
        sb.AppendLine($"Unmatched Source: {log.Result.Statistics.UnmatchedSourceCount}");
        sb.AppendLine($"Unmatched Dest:   {log.Result.Statistics.UnmatchedDestinationCount}");
        sb.AppendLine($"Validation Issues: {log.Result.Statistics.ValidationIssuesCount}");
        sb.AppendLine();

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Проход по уровням
        foreach (var level in log.Levels.OrderBy(l => l.Level))
        {
            sb.AppendLine($"╔═══ LEVEL {level.Level} ════════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║ Persons Processed: {level.PersonsProcessedAtLevel,-3} │ Families Examined: {level.FamiliesExaminedAtLevel,-3} │ New Mappings: {level.NewMappingsAtLevel,-3}     ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            foreach (var person in level.PersonsProcessed)
            {
                FormatPersonProcessing(sb, person);
                sb.AppendLine();
            }

            sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void FormatPersonProcessing(StringBuilder sb, PersonProcessingLog person)
    {
        sb.AppendLine($"┌─ Person: {person.SourceName} ({person.SourceId})");
        sb.AppendLine($"│  Mapped to: {person.DestinationName} ({person.DestinationId})");
        sb.AppendLine($"│  Via: {person.MappedVia} at Level {person.Level}");
        sb.AppendLine("│");

        // Families as Spouse
        if (person.FamiliesAsSpouse.Any())
        {
            sb.AppendLine($"│  ┌─ Families as Spouse/Parent ({person.FamiliesAsSpouse.Count}):");
            foreach (var family in person.FamiliesAsSpouse)
            {
                FormatFamilyMatch(sb, family, "│  │");
            }
        }

        // Families as Child
        if (person.FamiliesAsChild.Any())
        {
            sb.AppendLine($"│  ┌─ Families as Child ({person.FamiliesAsChild.Count}):");
            foreach (var family in person.FamiliesAsChild)
            {
                FormatFamilyMatch(sb, family, "│  │");
            }
        }

        sb.AppendLine("└─");
    }

    private void FormatFamilyMatch(StringBuilder sb, FamilyMatchAttemptLog family, string indent)
    {
        sb.AppendLine($"{indent}");
        sb.AppendLine($"{indent}  Family: {family.SourceFamilyId}");
        sb.AppendLine($"{indent}  Structure: H:{family.SourceStructure.HusbandName ?? "none"} + W:{family.SourceStructure.WifeName ?? "none"} → {family.SourceStructure.ChildCount} children");

        if (family.MatchResult == FamilyMatchResult.Matched)
        {
            sb.AppendLine($"{indent}  ✓ MATCHED → {family.MatchedDestFamilyId} (Score: {family.BestScore})");

            var bestCandidate = family.Candidates.FirstOrDefault(c => c.DestFamilyId == family.MatchedDestFamilyId);
            if (bestCandidate != null)
            {
                sb.AppendLine($"{indent}    Score Breakdown:");
                foreach (var component in bestCandidate.ScoreBreakdown)
                {
                    sb.AppendLine($"{indent}      • {component.Component}: +{component.Points} ({component.Description})");
                }
            }
        }
        else if (family.MatchResult == FamilyMatchResult.NoMatch)
        {
            sb.AppendLine($"{indent}  ✗ NO MATCH - {family.NoMatchReason}");
            if (family.Candidates.Any())
            {
                sb.AppendLine($"{indent}    Candidates examined: {family.Candidates.Count}");
                foreach (var candidate in family.Candidates.Take(3))
                {
                    sb.AppendLine($"{indent}      • {candidate.DestFamilyId}: Score={candidate.StructureScore}");
                }
            }
        }
        else if (family.MatchResult == FamilyMatchResult.Conflict)
        {
            sb.AppendLine($"{indent}  ⚠ CONFLICT - {family.NoMatchReason}");
            foreach (var candidate in family.Candidates.Where(c => c.HasConflict))
            {
                sb.AppendLine($"{indent}      • {candidate.DestFamilyId}: {candidate.ConflictReason}");
            }
        }
        else if (family.MatchResult == FamilyMatchResult.NoCandidates)
        {
            sb.AppendLine($"{indent}  - NO CANDIDATES");
        }
    }

    /// <summary>
    /// Форматировать только краткую статистику по уровням.
    /// </summary>
    public string FormatLevelSummary(WaveCompareLog log)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Level Summary:");
        sb.AppendLine("─────────────────────────────────────────────────────");
        sb.AppendLine("Level │ Persons │ Families │ New Mappings");
        sb.AppendLine("──────┼─────────┼──────────┼─────────────");

        foreach (var level in log.Levels.OrderBy(l => l.Level))
        {
            sb.AppendLine($"{level.Level,5} │ {level.PersonsProcessedAtLevel,7} │ {level.FamiliesExaminedAtLevel,8} │ {level.NewMappingsAtLevel,12}");
        }

        sb.AppendLine("─────────────────────────────────────────────────────");

        return sb.ToString();
    }

    /// <summary>
    /// Форматировать только персоны, которые не были распознаны.
    /// </summary>
    public string FormatUnmatched(WaveCompareLog log)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Unmatched Analysis:");
        sb.AppendLine("═══════════════════════════════════════════════════════");
        sb.AppendLine();

        // Найти персоны без сопоставлений в логе
        var processedPersons = log.Levels
            .SelectMany(l => l.PersonsProcessed)
            .ToList();

        // Анализировать попытки сопоставления
        sb.AppendLine("Persons with failed family matches:");
        sb.AppendLine("───────────────────────────────────────────────────────");

        foreach (var person in processedPersons)
        {
            var failedSpouse = person.FamiliesAsSpouse.Where(f => f.MatchResult != FamilyMatchResult.Matched);
            var failedChild = person.FamiliesAsChild.Where(f => f.MatchResult != FamilyMatchResult.Matched);

            if (failedSpouse.Any() || failedChild.Any())
            {
                sb.AppendLine($"• {person.SourceName} ({person.SourceId})");

                foreach (var family in failedSpouse)
                {
                    sb.AppendLine($"    As Spouse: {family.SourceFamilyId} - {family.MatchResult} - {family.NoMatchReason}");
                }

                foreach (var family in failedChild)
                {
                    sb.AppendLine($"    As Child: {family.SourceFamilyId} - {family.MatchResult} - {family.NoMatchReason}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Форматировать валидационные ошибки.
    /// </summary>
    public string FormatValidationIssues(WaveCompareLog log)
    {
        var sb = new StringBuilder();

        if (!log.Result.ValidationIssues.Any())
        {
            return "No validation issues found.";
        }

        sb.AppendLine("Validation Issues:");
        sb.AppendLine("═══════════════════════════════════════════════════════");
        sb.AppendLine();

        var bySeverity = log.Result.ValidationIssues
            .GroupBy(i => i.Severity)
            .OrderByDescending(g => g.Key);

        foreach (var group in bySeverity)
        {
            sb.AppendLine($"[{group.Key}] ({group.Count()} issues)");
            sb.AppendLine("───────────────────────────────────────────────────────");

            foreach (var issue in group.Take(20))
            {
                sb.AppendLine($"  • {issue.Type}: {issue.Message}");
                sb.AppendLine($"    {issue.SourceId} → {issue.DestId}");
            }

            if (group.Count() > 20)
            {
                sb.AppendLine($"  ... and {group.Count() - 20} more");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
