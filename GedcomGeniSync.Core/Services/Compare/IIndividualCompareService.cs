using GedcomGeniSync.Models;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

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
    /// <param name="existingMatches">Existing mappings from previous iterations or anchors</param>
    /// <param name="sourceFamilies">Families from source GEDCOM (for resolving ambiguous matches)</param>
    /// <param name="destFamilies">Families from destination GEDCOM (for resolving ambiguous matches)</param>
    /// <returns>Individual comparison result</returns>
    IndividualCompareResult CompareIndividuals(
        Dictionary<string, PersonRecord> sourcePersons,
        Dictionary<string, PersonRecord> destPersons,
        CompareOptions options,
        IReadOnlyDictionary<string, string>? existingMatches = null,
        IReadOnlyDictionary<string, Family>? sourceFamilies = null,
        IReadOnlyDictionary<string, Family>? destFamilies = null);
}
