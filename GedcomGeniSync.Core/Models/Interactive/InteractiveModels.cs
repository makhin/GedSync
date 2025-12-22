using GedcomGeniSync.Models;

namespace GedcomGeniSync.Core.Models.Interactive;

/// <summary>
/// Request for user confirmation on a low-confidence match
/// </summary>
public class InteractiveConfirmationRequest
{
    /// <summary>Source person that needs to be matched</summary>
    public required PersonRecord SourcePerson { get; init; }

    /// <summary>List of candidate matches with scores</summary>
    public required List<CandidateMatch> Candidates { get; init; }

    /// <summary>How this person was found (e.g., "Child of Ivan Petrov")</summary>
    public required string FoundVia { get; init; }

    /// <summary>BFS level from anchor</summary>
    public required int Level { get; init; }

    /// <summary>Maximum number of candidates to show</summary>
    public int MaxCandidates { get; init; } = 5;
}

/// <summary>
/// A candidate match with score and details
/// </summary>
public class CandidateMatch
{
    /// <summary>Candidate person from destination tree</summary>
    public required PersonRecord Person { get; init; }

    /// <summary>Match score (0-100)</summary>
    public required int Score { get; init; }

    /// <summary>Detailed breakdown of how the score was calculated</summary>
    public required ScoreBreakdown Breakdown { get; init; }
}

/// <summary>
/// Detailed breakdown of match score calculation
/// </summary>
public class ScoreBreakdown
{
    public double FirstNameScore { get; init; }
    public string? FirstNameDetails { get; init; }

    public double LastNameScore { get; init; }
    public string? LastNameDetails { get; init; }

    public double BirthDateScore { get; init; }
    public string? BirthDateDetails { get; init; }

    public double BirthPlaceScore { get; init; }
    public string? BirthPlaceDetails { get; init; }

    public double GenderScore { get; init; }

    public int ParentsMatching { get; init; }
    public int ParentsTotal { get; init; }

    public int ChildrenMatching { get; init; }
    public int ChildrenTotal { get; init; }

    public int SiblingsMatching { get; init; }
    public int SiblingsTotal { get; init; }

    public bool SpouseMatches { get; init; }
}

/// <summary>
/// Result of user confirmation
/// </summary>
public class InteractiveConfirmationResult
{
    /// <summary>User decision</summary>
    public required UserDecision Decision { get; init; }

    /// <summary>Selected candidate (if confirmed)</summary>
    public PersonRecord? SelectedCandidate { get; init; }

    /// <summary>Original score of selected candidate</summary>
    public int? SelectedScore { get; init; }
}

/// <summary>
/// User decision on a match
/// </summary>
public enum UserDecision
{
    /// <summary>User confirmed one of the candidates</summary>
    Confirmed,

    /// <summary>User rejected all candidates (not a match)</summary>
    Rejected,

    /// <summary>User skipped decision (to decide later)</summary>
    Skipped
}
