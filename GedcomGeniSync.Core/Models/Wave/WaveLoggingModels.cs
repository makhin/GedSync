using GedcomGeniSync.Models;

namespace GedcomGeniSync.Core.Models.Wave;

/// <summary>
/// Complete log of wave comparison execution.
/// </summary>
public record WaveCompareLog
{
    public required string SourceFile { get; init; }
    public required string DestinationFile { get; init; }
    public required string AnchorSourceId { get; init; }
    public required string AnchorDestId { get; init; }
    public required int MaxLevel { get; init; }
    public required ThresholdStrategy Strategy { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required List<WaveLevelLog> Levels { get; init; } = new();
    public required WaveCompareResult Result { get; init; }
}

/// <summary>
/// Log for a single BFS level/wave.
/// </summary>
public record WaveLevelLog
{
    public required int Level { get; init; }
    public required int PersonsProcessedAtLevel { get; init; }
    public required int FamiliesExaminedAtLevel { get; init; }
    public required int NewMappingsAtLevel { get; init; }
    public required List<PersonProcessingLog> PersonsProcessed { get; init; } = new();
}

/// <summary>
/// Log for processing a single person at a wave level.
/// </summary>
public record PersonProcessingLog
{
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required string DestinationId { get; init; }
    public required string DestinationName { get; init; }
    public required RelationType MappedVia { get; init; }
    public required int Level { get; init; }
    public required List<FamilyMatchAttemptLog> FamiliesAsSpouse { get; init; } = new();
    public required List<FamilyMatchAttemptLog> FamiliesAsChild { get; init; } = new();
}

/// <summary>
/// Log for attempting to match a family.
/// </summary>
public record FamilyMatchAttemptLog
{
    public required string SourceFamilyId { get; init; }
    public required FamilyStructureDescription SourceStructure { get; init; }
    public required List<CandidateFamilyLog> Candidates { get; init; } = new();
    public required FamilyMatchResult MatchResult { get; init; }
    public string? MatchedDestFamilyId { get; init; }
    public int? BestScore { get; init; }
    public string? NoMatchReason { get; init; }
}

/// <summary>
/// Describes family structure for logging.
/// </summary>
public record FamilyStructureDescription
{
    public string? HusbandId { get; init; }
    public string? HusbandName { get; init; }
    public string? WifeId { get; init; }
    public string? WifeName { get; init; }
    public required List<string> ChildIds { get; init; } = new();
    public required List<string> ChildNames { get; init; } = new();
    public int ChildCount => ChildIds.Count;
}

/// <summary>
/// Log for a candidate destination family.
/// </summary>
public record CandidateFamilyLog
{
    public required string DestFamilyId { get; init; }
    public required FamilyStructureDescription Structure { get; init; }
    public required int StructureScore { get; init; }
    public required List<ScoreComponent> ScoreBreakdown { get; init; } = new();
    public bool HasConflict { get; init; }
    public string? ConflictReason { get; init; }
}

/// <summary>
/// Component of structural matching score.
/// </summary>
public record ScoreComponent
{
    public required string Component { get; init; }
    public required int Points { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Result of family matching attempt.
/// </summary>
public enum FamilyMatchResult
{
    Matched,
    NoMatch,
    Conflict,
    NoCandidates
}

/// <summary>
/// Log for attempting to match a person within a family context.
/// </summary>
public record PersonMatchAttemptLog
{
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required RelationType RelationType { get; init; }
    public required string FamilyContext { get; init; }
    public required List<PersonCandidateLog> Candidates { get; init; } = new();
    public required PersonMatchResult MatchResult { get; init; }
    public string? MatchedDestId { get; init; }
    public string? MatchedDestName { get; init; }
    public int? MatchScore { get; init; }
    public int? ThresholdUsed { get; init; }
    public string? NoMatchReason { get; init; }
}

/// <summary>
/// Log for a candidate destination person.
/// </summary>
public record PersonCandidateLog
{
    public required string DestId { get; init; }
    public required string DestName { get; init; }
    public required int Score { get; init; }
    public required int Threshold { get; init; }
    public required bool PassesThreshold { get; init; }
    public required List<MatchAttribute> AttributeComparisons { get; init; } = new();
    public required ValidationResult ValidationResult { get; init; }
    public required bool Selected { get; init; }
    public string? RejectionReason { get; init; }
}

/// <summary>
/// Comparison of a single attribute between source and destination.
/// </summary>
public record MatchAttribute
{
    public required string AttributeName { get; init; }
    public required string SourceValue { get; init; }
    public required string DestValue { get; init; }
    public required double SimilarityScore { get; init; }
    public required int ContributionToTotal { get; init; }
}

/// <summary>
/// Result of person matching attempt.
/// </summary>
public enum PersonMatchResult
{
    Matched,
    BelowThreshold,
    ValidationFailed,
    AlreadyMapped,
    NoCandidates
}

/// <summary>
/// Log for greedy matching algorithm (children matching).
/// </summary>
public record GreedyMatchLog
{
    public required string FamilyContext { get; init; }
    public required int SourceChildrenCount { get; init; }
    public required int DestChildrenCount { get; init; }
    public required List<List<int>> ScoreMatrix { get; init; } = new();
    public required List<GreedyMatchStep> Steps { get; init; } = new();
    public required int TotalPairs { get; init; }
}

/// <summary>
/// Single step in greedy matching algorithm.
/// </summary>
public record GreedyMatchStep
{
    public required int StepNumber { get; init; }
    public required string SourceChildId { get; init; }
    public required string SourceChildName { get; init; }
    public required string DestChildId { get; init; }
    public required string DestChildName { get; init; }
    public required int Score { get; init; }
    public required int Threshold { get; init; }
    public required bool Accepted { get; init; }
    public string? RejectionReason { get; init; }
}
