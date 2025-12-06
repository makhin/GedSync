using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for fuzzy matching service
/// Compares PersonRecords using various algorithms optimized for genealogical data
/// </summary>
public interface IFuzzyMatcherService
{
    /// <summary>
    /// Compare two persons and return match score with detailed reasoning
    /// </summary>
    /// <param name="source">Source person record</param>
    /// <param name="target">Target person record to compare against</param>
    /// <returns>Match candidate with score (0-100) and matching reasons</returns>
    MatchCandidate Compare(PersonRecord source, PersonRecord target);
}
