using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing individual (INDI) records between source and destination GEDCOM files
/// </summary>
public interface IIndividualCompareService
{
    /// <summary>
    /// Compare individuals from source and destination GEDCOM files
    /// </summary>
    /// <param name="sourcePersons">Persons from source GEDCOM (e.g., MyHeritage)</param>
    /// <param name="destPersons">Persons from destination GEDCOM (e.g., Geni)</param>
    /// <param name="options">Comparison options</param>
    /// <returns>Individual comparison result</returns>
    IndividualCompareResult CompareIndividuals(
        Dictionary<string, PersonRecord> sourcePersons,
        Dictionary<string, PersonRecord> destPersons,
        CompareOptions options,
        IReadOnlyDictionary<string, string>? existingMatches = null);
}
