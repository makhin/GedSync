using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing individual (INDI) records between source and destination GEDCOM files
/// Implements matching logic with RFN/INDI_ID priority and fuzzy matching fallback
/// </summary>
public class IndividualCompareService : IIndividualCompareService
{
    private readonly ILogger<IndividualCompareService> _logger;
    private readonly IPersonFieldComparer _fieldComparer;
    private readonly IFuzzyMatcherService _fuzzyMatcher;

    public IndividualCompareService(
        ILogger<IndividualCompareService> logger,
        IPersonFieldComparer fieldComparer,
        IFuzzyMatcherService fuzzyMatcher)
    {
        _logger = logger;
        _fieldComparer = fieldComparer;
        _fuzzyMatcher = fuzzyMatcher;
    }

    public IndividualCompareResult CompareIndividuals(
        Dictionary<string, PersonRecord> sourcePersons,
        Dictionary<string, PersonRecord> destPersons,
        CompareOptions options)
    {
        _logger.LogInformation("Starting individual comparison. Source: {SourceCount}, Destination: {DestCount}",
            sourcePersons.Count, destPersons.Count);

        // Set person dictionaries for family relations comparison
        _fuzzyMatcher.SetPersonDictionaries(sourcePersons, destPersons);
        _logger.LogInformation("Person dictionaries set for family-based matching");

        var matchedNodes = ImmutableList.CreateBuilder<MatchedNode>();
        var nodesToUpdate = ImmutableList.CreateBuilder<NodeToUpdate>();
        var nodesToAdd = ImmutableList.CreateBuilder<NodeToAdd>();
        var nodesToDelete = ImmutableList.CreateBuilder<NodeToDelete>();
        var ambiguousMatches = ImmutableList.CreateBuilder<AmbiguousMatch>();

        // Track which destination persons have been matched
        var matchedDestinationIds = new HashSet<string>();

        // Step 1: Process each source person
        foreach (var sourcePerson in sourcePersons.Values)
        {
            var matchResult = FindBestMatch(sourcePerson, destPersons, options);

            if (matchResult == null)
            {
                // No match found - add to ToAdd list
                nodesToAdd.Add(new NodeToAdd
                {
                    SourceId = sourcePerson.Id,
                    PersonData = ConvertToPersonData(sourcePerson),
                    RelatedToNodeId = null, // Will be calculated later based on depth
                    RelationType = null,
                    DepthFromExisting = 0 // Will be calculated later
                });

                _logger.LogDebug("No match found for {SourceId} - {PersonSummary}",
                    sourcePerson.Id, GetPersonSummary(sourcePerson));
            }
            else if (matchResult.IsAmbiguous)
            {
                // Multiple candidates - ambiguous match
                ambiguousMatches.Add(new AmbiguousMatch
                {
                    SourceId = sourcePerson.Id,
                    PersonSummary = GetPersonSummary(sourcePerson),
                    Candidates = matchResult.Candidates
                });

                _logger.LogWarning("Ambiguous match for {SourceId} - {PersonSummary}: {CandidateCount} candidates",
                    sourcePerson.Id, GetPersonSummary(sourcePerson), matchResult.Candidates.Count);
            }
            else
            {
                // Single match found
                var destPerson = matchResult.MatchedPerson!;
                matchedDestinationIds.Add(destPerson.Id);

                // Compare fields to see if update is needed
                var fieldDifferences = _fieldComparer.CompareFields(sourcePerson, destPerson);

                if (fieldDifferences.Count == 0)
                {
                    // Perfect match - no updates needed
                    matchedNodes.Add(new MatchedNode
                    {
                        SourceId = sourcePerson.Id,
                        DestinationId = destPerson.Id,
                        GeniProfileId = destPerson.GeniProfileId,
                        MatchScore = matchResult.Score,
                        MatchedBy = matchResult.MatchedBy,
                        PersonSummary = GetPersonSummary(sourcePerson)
                    });

                    _logger.LogDebug("Perfect match: {SourceId} -> {DestId} (score: {Score}, by: {MatchedBy})",
                        sourcePerson.Id, destPerson.Id, matchResult.Score, matchResult.MatchedBy);
                }
                else
                {
                    // Match found but needs updates
                    nodesToUpdate.Add(new NodeToUpdate
                    {
                        SourceId = sourcePerson.Id,
                        DestinationId = destPerson.Id,
                        GeniProfileId = destPerson.GeniProfileId,
                        MatchScore = matchResult.Score,
                        MatchedBy = matchResult.MatchedBy,
                        PersonSummary = GetPersonSummary(sourcePerson),
                        FieldsToUpdate = fieldDifferences
                    });

                    _logger.LogDebug("Match with updates: {SourceId} -> {DestId} ({FieldCount} fields to update)",
                        sourcePerson.Id, destPerson.Id, fieldDifferences.Count);
                }
            }
        }

        // Step 2: Find destination persons that weren't matched (candidates for deletion)
        if (options.IncludeDeleteSuggestions)
        {
            foreach (var destPerson in destPersons.Values)
            {
                if (!matchedDestinationIds.Contains(destPerson.Id))
                {
                    nodesToDelete.Add(new NodeToDelete
                    {
                        DestinationId = destPerson.Id,
                        GeniProfileId = destPerson.GeniProfileId,
                        PersonSummary = GetPersonSummary(destPerson),
                        Reason = "Not found in source within comparison scope"
                    });

                    _logger.LogDebug("Candidate for deletion: {DestId} - {PersonSummary}",
                        destPerson.Id, GetPersonSummary(destPerson));
                }
            }
        }

        _logger.LogInformation(
            "Individual comparison complete. Matched: {Matched}, ToUpdate: {ToUpdate}, ToAdd: {ToAdd}, ToDelete: {ToDelete}, Ambiguous: {Ambiguous}",
            matchedNodes.Count, nodesToUpdate.Count, nodesToAdd.Count, nodesToDelete.Count, ambiguousMatches.Count);

        return new IndividualCompareResult
        {
            MatchedNodes = matchedNodes.ToImmutable(),
            NodesToUpdate = nodesToUpdate.ToImmutable(),
            NodesToAdd = nodesToAdd.ToImmutable(),
            NodesToDelete = nodesToDelete.ToImmutable(),
            AmbiguousMatches = ambiguousMatches.ToImmutable()
        };
    }

    private MatchResult? FindBestMatch(
        PersonRecord source,
        Dictionary<string, PersonRecord> destPersons,
        CompareOptions options)
    {
        var candidates = new List<(PersonRecord person, int score, string matchedBy)>();

        // Strategy 1: Try RFN exact match (highest priority)
        if (!string.IsNullOrEmpty(source.GeniProfileId))
        {
            foreach (var dest in destPersons.Values)
            {
                if (GeniIdHelper.IsSameGeniProfile(source.GeniProfileId, dest.GeniProfileId))
                {
                    return new MatchResult
                    {
                        MatchedPerson = dest,
                        Score = 100,
                        MatchedBy = "RFN",
                        IsAmbiguous = false
                    };
                }
            }
        }

        // Strategy 2: Fuzzy matching (name-based comparison)
        foreach (var dest in destPersons.Values)
        {
            var matchCandidate = _fuzzyMatcher.Compare(source, dest);
            if (matchCandidate.Score >= options.MatchThreshold)
            {
                candidates.Add((dest, (int)matchCandidate.Score, "Fuzzy"));
            }
        }

        if (candidates.Count == 0)
        {
            return null; // No match found
        }

        if (candidates.Count == 1 || !options.RequireUniqueMatch)
        {
            var best = candidates.OrderByDescending(c => c.score).First();
            return new MatchResult
            {
                MatchedPerson = best.person,
                Score = best.score,
                MatchedBy = best.matchedBy,
                IsAmbiguous = false
            };
        }

        // Multiple candidates - check if we should treat as ambiguous
        var topScore = candidates.Max(c => c.score);
        var topCandidates = candidates.Where(c => c.score == topScore).ToList();

        if (topCandidates.Count > 1)
        {
            // Truly ambiguous - multiple candidates with same top score
            return new MatchResult
            {
                IsAmbiguous = true,
                Candidates = topCandidates
                    .Select(c => new Models.MatchCandidate
                    {
                        Source = source,
                        Target = c.person,
                        Score = c.score
                    })
                    .ToImmutableList()
            };
        }

        // One clear winner
        var winner = topCandidates.First();
        return new MatchResult
        {
            MatchedPerson = winner.person,
            Score = winner.score,
            MatchedBy = winner.matchedBy,
            IsAmbiguous = false
        };
    }

    private string GetPersonSummary(PersonRecord person)
    {
        var name = person.FullName;
        if (string.IsNullOrWhiteSpace(name))
            name = person.Id;

        var birth = person.BirthYear.HasValue ? $" (*{person.BirthYear})" : "";
        var death = person.DeathYear.HasValue ? $" (†{person.DeathYear})" : "";

        return $"{name}{birth}{death}";
    }

    private PersonData ConvertToPersonData(PersonRecord person)
    {
        return new PersonData
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,
            MiddleName = person.MiddleName,
            Suffix = person.Suffix,
            Nickname = person.Nickname,
            Gender = person.Gender.ToString(),
            BirthDate = person.BirthDate?.ToGeniFormat(),
            BirthPlace = person.BirthPlace,
            DeathDate = person.DeathDate?.ToGeniFormat(),
            DeathPlace = person.DeathPlace,
            BurialDate = person.BurialDate?.ToGeniFormat(),
            BurialPlace = person.BurialPlace,
            PhotoUrl = person.PhotoUrls.FirstOrDefault()
        };
    }

    private class MatchResult
    {
        public PersonRecord? MatchedPerson { get; init; }
        public int Score { get; init; }
        public string MatchedBy { get; init; } = "";
        public bool IsAmbiguous { get; init; }
        public ImmutableList<Models.MatchCandidate> Candidates { get; init; } = ImmutableList<Models.MatchCandidate>.Empty;
    }
}
