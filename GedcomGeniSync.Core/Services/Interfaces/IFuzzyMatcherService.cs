using System.Collections.Generic;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for fuzzy matching service
/// Compares PersonRecords using various algorithms optimized for genealogical data
/// </summary>
public interface IFuzzyMatcherService
{
    /// <summary>
    /// Set person dictionaries for family relations comparison
    /// This allows comparing family members by name when IDs don't match between different GEDCOM files
    /// </summary>
    /// <param name="sourcePersons">Dictionary of source persons by ID</param>
    /// <param name="destPersons">Dictionary of destination persons by ID</param>
    void SetPersonDictionaries(
        Dictionary<string, PersonRecord>? sourcePersons,
        Dictionary<string, PersonRecord>? destPersons);

    /// <summary>
    /// Compare two persons and return match score with detailed reasoning
    /// </summary>
    /// <param name="source">Source person record</param>
    /// <param name="target">Target person record to compare against</param>
    /// <returns>Match candidate with score (0-100) and matching reasons</returns>
    MatchCandidate Compare(PersonRecord source, PersonRecord target);

    /// <summary>
    /// Find best matches for a source person within a set of candidates.
    /// </summary>
    /// <param name="source">Person to match.</param>
    /// <param name="candidates">Potential matches.</param>
    /// <param name="minScore">Minimum score threshold for inclusion.</param>
    /// <returns>Sorted list of candidates with scores and reasons.</returns>
    List<MatchCandidate> FindMatches(
        PersonRecord source,
        IEnumerable<PersonRecord> candidates,
        int minScore = 0);
}
