using System.Collections.Immutable;

namespace GedcomGeniSync.Models;

#region Root Compare Result

/// <summary>
/// Root comparison result containing all comparison data
/// </summary>
public record CompareResult
{
    /// <summary>
    /// Source GEDCOM file path (MyHeritage)
    /// </summary>
    public required string SourceFile { get; init; }

    /// <summary>
    /// Destination GEDCOM file path (Geni export)
    /// </summary>
    public required string DestinationFile { get; init; }

    /// <summary>
    /// When the comparison was performed
    /// </summary>
    public DateTime ComparedAt { get; init; }

    /// <summary>
    /// Anchor information
    /// </summary>
    public required AnchorInfo Anchors { get; init; }

    /// <summary>
    /// Comparison options used
    /// </summary>
    public required CompareOptions Options { get; init; }

    /// <summary>
    /// Statistics about the comparison
    /// </summary>
    public required CompareStatistics Statistics { get; init; }

    /// <summary>
    /// Individual (INDI) comparison results
    /// </summary>
    public required IndividualCompareResult Individuals { get; init; }

    /// <summary>
    /// Family (FAM) comparison results
    /// </summary>
    public required FamilyCompareResult Families { get; init; }

    /// <summary>
    /// Detailed results for each comparison iteration
    /// </summary>
    public ImmutableList<CompareIterationResult> Iterations { get; init; } = ImmutableList<CompareIterationResult>.Empty;
}

/// <summary>
/// Anchor person information
/// </summary>
public record AnchorInfo
{
    /// <summary>
    /// Anchor person ID in source GEDCOM
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Anchor person ID in destination GEDCOM
    /// </summary>
    public required string DestinationId { get; init; }

    /// <summary>
    /// Geni Profile ID of anchor person (from RFN)
    /// </summary>
    public string? GeniProfileId { get; init; }

    /// <summary>
    /// Whether anchor match was confirmed
    /// </summary>
    public bool MatchConfirmed { get; init; }
}

/// <summary>
/// Options used for comparison
/// </summary>
public record CompareOptions
{
    /// <summary>
    /// Anchor source ID (required)
    /// </summary>
    public required string AnchorSourceId { get; init; }

    /// <summary>
    /// Anchor destination ID (required)
    /// </summary>
    public required string AnchorDestinationId { get; init; }

    /// <summary>
    /// Depth of new nodes to add from existing matched nodes (default: 1)
    /// </summary>
    public int NewNodeDepth { get; init; } = 1;

    /// <summary>
    /// Match threshold score 0-100 (default: 70)
    /// </summary>
    public int MatchThreshold { get; init; } = 70;

    /// <summary>
    /// Whether to include delete suggestions (default: false)
    /// </summary>
    public bool IncludeDeleteSuggestions { get; init; } = false;

    /// <summary>
    /// Whether to require unique matches (default: true)
    /// </summary>
    public bool RequireUniqueMatch { get; init; } = true;
}

/// <summary>
/// Statistics about the comparison
/// </summary>
public record CompareStatistics
{
    public required IndividualStats Individuals { get; init; }
    public required FamilyStats Families { get; init; }
}

public record CompareIterationResult
{
    public int Iteration { get; init; }
    public required IndividualCompareResult Individuals { get; init; }
    public required FamilyCompareResult Families { get; init; }
    public required CompareStatistics Statistics { get; init; }
    public int NewPersonMappings { get; init; }
}

public record IndividualStats
{
    public int TotalSource { get; init; }
    public int TotalDestination { get; init; }
    public int Matched { get; init; }
    public int ToUpdate { get; init; }
    public int ToAdd { get; init; }
    public int ToDelete { get; init; }
    public int Ambiguous { get; init; }
}

public record FamilyStats
{
    public int TotalSource { get; init; }
    public int TotalDestination { get; init; }
    public int Matched { get; init; }
    public int ToUpdate { get; init; }
    public int ToAdd { get; init; }
    public int ToDelete { get; init; }
}

#endregion

#region Individual Compare Results

/// <summary>
/// Individual (INDI) comparison results
/// </summary>
public record IndividualCompareResult
{
    public ImmutableList<MatchedNode> MatchedNodes { get; init; } = ImmutableList<MatchedNode>.Empty;
    public ImmutableList<NodeToUpdate> NodesToUpdate { get; init; } = ImmutableList<NodeToUpdate>.Empty;
    public ImmutableList<NodeToAdd> NodesToAdd { get; init; } = ImmutableList<NodeToAdd>.Empty;
    public ImmutableList<NodeToDelete> NodesToDelete { get; init; } = ImmutableList<NodeToDelete>.Empty;
    public ImmutableList<AmbiguousMatch> AmbiguousMatches { get; init; } = ImmutableList<AmbiguousMatch>.Empty;
}

/// <summary>
/// Matched node with no updates needed
/// </summary>
public record MatchedNode
{
    /// <summary>
    /// Source GEDCOM ID (e.g., "@I123@")
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Destination GEDCOM ID (e.g., "@I456@")
    /// </summary>
    public required string DestinationId { get; init; }

    /// <summary>
    /// Geni Profile ID from RFN tag (e.g., "profile-12345")
    /// </summary>
    public string? GeniProfileId { get; init; }

    /// <summary>
    /// Match score 0-100
    /// </summary>
    public int MatchScore { get; init; }

    /// <summary>
    /// How the match was made: "RFN" | "INDI_ID" | "Fuzzy"
    /// </summary>
    public required string MatchedBy { get; init; }

    /// <summary>
    /// Human-readable person summary (e.g., "Иванов Иван (1950-2020)")
    /// </summary>
    public required string PersonSummary { get; init; }
}

/// <summary>
/// Node that exists in both but needs updates
/// </summary>
public record NodeToUpdate
{
    public required string SourceId { get; init; }
    public required string DestinationId { get; init; }
    public string? GeniProfileId { get; init; }
    public int MatchScore { get; init; }
    public required string MatchedBy { get; init; }
    public required string PersonSummary { get; init; }
    public ImmutableList<FieldDiff> FieldsToUpdate { get; init; } = ImmutableList<FieldDiff>.Empty;
}

/// <summary>
/// Field difference for updates
/// </summary>
public record FieldDiff
{
    /// <summary>
    /// Field name (e.g., "BirthPlace", "PhotoUrl")
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// Value from source
    /// </summary>
    public string? SourceValue { get; init; }

    /// <summary>
    /// Value from destination
    /// </summary>
    public string? DestinationValue { get; init; }

    /// <summary>
    /// Action to take: Add | Update | AddPhoto | UpdatePhoto | PhotoMatch
    /// </summary>
    public FieldAction Action { get; init; }

    /// <summary>
    /// Similarity score for photo comparisons (0.0 - 1.0).
    /// </summary>
    public double? PhotoSimilarity { get; init; }

    /// <summary>
    /// Local path to cached photo for upload.
    /// </summary>
    public string? LocalPhotoPath { get; init; }
}

public enum FieldAction
{
    Add,
    Update,
    AddPhoto,
    UpdatePhoto,
    PhotoMatch
}

/// <summary>
/// Node to add to destination
/// </summary>
public record NodeToAdd
{
    public required string SourceId { get; init; }
    public required PersonData PersonData { get; init; }

    /// <summary>
    /// ID of existing matched node this is related to
    /// </summary>
    public string? RelatedToNodeId { get; init; }

    /// <summary>
    /// Relationship type to the related node
    /// </summary>
    public CompareRelationType? RelationType { get; init; }

    /// <summary>
    /// Depth from matched/anchor nodes
    /// </summary>
    public int DepthFromExisting { get; init; }
}

/// <summary>
/// Person data for new nodes
/// </summary>
public record PersonData
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? MaidenName { get; init; }
    public string? MiddleName { get; init; }
    public string? Suffix { get; init; }
    public string? Nickname { get; init; }
    public string? Gender { get; init; }
    public string? BirthDate { get; init; }
    public string? BirthPlace { get; init; }
    public string? DeathDate { get; init; }
    public string? DeathPlace { get; init; }
    public string? BurialDate { get; init; }
    public string? BurialPlace { get; init; }
    public string? PhotoUrl { get; init; }
    public string? Occupation { get; init; }
    public string? ResidenceAddress { get; init; }
    public ImmutableList<string> Notes { get; init; } = ImmutableList<string>.Empty;
}

public enum CompareRelationType
{
    Parent,
    Child,
    Spouse,
    Sibling
}

/// <summary>
/// Node to delete from destination
/// </summary>
public record NodeToDelete
{
    public required string DestinationId { get; init; }
    public string? GeniProfileId { get; init; }
    public required string PersonSummary { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Ambiguous match with multiple candidates
/// </summary>
public record AmbiguousMatch
{
    public required string SourceId { get; init; }
    public required string PersonSummary { get; init; }
    public ImmutableList<Models.MatchCandidate> Candidates { get; init; } = ImmutableList<Models.MatchCandidate>.Empty;
}

/// <summary>
/// Match candidate for ambiguous matches
/// Note: This is different from PersonRecord.MatchCandidate
/// </summary>
public record CompareMatchCandidate
{
    public required string DestinationId { get; init; }
    public int Score { get; init; }
    public required string Summary { get; init; }
}

#endregion

#region Family Compare Results

/// <summary>
/// Family (FAM) comparison results
/// </summary>
public record FamilyCompareResult
{
    public ImmutableList<MatchedFamily> MatchedFamilies { get; init; } = ImmutableList<MatchedFamily>.Empty;
    public ImmutableList<FamilyToUpdate> FamiliesToUpdate { get; init; } = ImmutableList<FamilyToUpdate>.Empty;
    public ImmutableList<FamilyToAdd> FamiliesToAdd { get; init; } = ImmutableList<FamilyToAdd>.Empty;
    public ImmutableList<FamilyToDelete> FamiliesToDelete { get; init; } = ImmutableList<FamilyToDelete>.Empty;
    public ImmutableDictionary<string, string> NewPersonMappings { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Matched family with no updates needed
/// </summary>
public record MatchedFamily
{
    public required string SourceFamId { get; init; }
    public required string DestinationFamId { get; init; }
    public string? HusbandSourceId { get; init; }
    public string? HusbandDestinationId { get; init; }
    public string? WifeSourceId { get; init; }
    public string? WifeDestinationId { get; init; }

    /// <summary>
    /// Mapping of children IDs: source ID -> destination ID
    /// </summary>
    public ImmutableDictionary<string, string> ChildrenMapping { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Family that needs updates
/// </summary>
public record FamilyToUpdate
{
    public required string SourceFamId { get; init; }
    public required string DestinationFamId { get; init; }

    /// <summary>
    /// Children from source that are missing in destination FAM
    /// </summary>
    public ImmutableList<string> MissingChildren { get; init; } = ImmutableList<string>.Empty;

    public FieldDiff? MarriageDate { get; init; }
    public FieldDiff? MarriagePlace { get; init; }
    public FieldDiff? DivorceDate { get; init; }
}

/// <summary>
/// Family to add to destination
/// </summary>
public record FamilyToAdd
{
    public required string SourceFamId { get; init; }

    /// <summary>
    /// Husband ID (mapped destination ID or source ID)
    /// </summary>
    public string? HusbandId { get; init; }

    /// <summary>
    /// Wife ID (mapped destination ID or source ID)
    /// </summary>
    public string? WifeId { get; init; }

    /// <summary>
    /// Children IDs
    /// </summary>
    public ImmutableList<string> ChildrenIds { get; init; } = ImmutableList<string>.Empty;

    public string? MarriageDate { get; init; }
    public string? MarriagePlace { get; init; }
}

/// <summary>
/// Family to delete from destination
/// </summary>
public record FamilyToDelete
{
    public required string DestinationFamId { get; init; }
    public required string Reason { get; init; }
}

#endregion

#region Mapping Validation Models

/// <summary>
/// Validation result for person mappings
/// </summary>
public record ValidationResult
{
    public ImmutableList<MappingIssue> Issues { get; init; } = ImmutableList<MappingIssue>.Empty;

    /// <summary>
    /// Whether validation passed (no high severity issues)
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == IssueSeverity.High);

    /// <summary>
    /// Count of issues by severity
    /// </summary>
    public int HighSeverityCount => Issues.Count(i => i.Severity == IssueSeverity.High);
    public int MediumSeverityCount => Issues.Count(i => i.Severity == IssueSeverity.Medium);
    public int LowSeverityCount => Issues.Count(i => i.Severity == IssueSeverity.Low);
}

/// <summary>
/// Issue found during mapping validation
/// </summary>
public record MappingIssue
{
    public required string SourceId { get; init; }
    public required string DestId { get; init; }
    public required IssueType Type { get; init; }
    public required IssueSeverity Severity { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Type of validation issue
/// </summary>
public enum IssueType
{
    /// <summary>
    /// Gender mismatch between source and destination
    /// </summary>
    GenderMismatch,

    /// <summary>
    /// Birth/death dates contradict (e.g., birth year difference > 5 years)
    /// </summary>
    DateContradiction,

    /// <summary>
    /// Family role inconsistency (e.g., child in one, parent in another)
    /// </summary>
    FamilyRoleInconsistency,

    /// <summary>
    /// Person appears as both child and parent in same family
    /// </summary>
    GenerationalInconsistency,

    /// <summary>
    /// Multiple persons from source mapped to same destination
    /// </summary>
    DuplicateMapping
}

/// <summary>
/// Severity level for validation issues
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Critical issue - mapping should be removed
    /// </summary>
    High,

    /// <summary>
    /// Suspicious issue - should be reviewed
    /// </summary>
    Medium,

    /// <summary>
    /// Minor issue - informational only
    /// </summary>
    Low
}

/// <summary>
/// Person mapping with confidence level
/// </summary>
public record MappingEntry
{
    public required string SourceId { get; init; }
    public required string DestId { get; init; }

    /// <summary>
    /// Confidence level 0.0 - 1.0
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// How the match was made: "RFN", "Fuzzy", "Family", etc.
    /// </summary>
    public required string MatchedBy { get; init; }

    /// <summary>
    /// Iteration when mapping was found
    /// </summary>
    public int IterationFound { get; init; }

    /// <summary>
    /// Match score (0-100)
    /// </summary>
    public int MatchScore { get; init; }
}

#endregion
