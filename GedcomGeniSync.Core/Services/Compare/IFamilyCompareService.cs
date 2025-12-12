using GedcomGeniSync.Models;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing family (FAM) records between source and destination GEDCOM files
/// </summary>
public interface IFamilyCompareService
{
    /// <summary>
    /// Compare families from source and destination GEDCOM files
    /// Requires individual matching results to map family members
    /// </summary>
    /// <param name="sourceFamilies">Families from source GEDCOM</param>
    /// <param name="destFamilies">Families from destination GEDCOM</param>
    /// <param name="individualResult">Results from individual comparison (for ID mapping)</param>
    /// <param name="options">Comparison options</param>
    /// <returns>Family comparison result</returns>
    FamilyCompareResult CompareFamilies(
        Dictionary<string, Family> sourceFamilies,
        Dictionary<string, Family> destFamilies,
        IndividualCompareResult individualResult,
        CompareOptions options);
}
