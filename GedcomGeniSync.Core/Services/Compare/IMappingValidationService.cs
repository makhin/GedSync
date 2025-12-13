using GedcomGeniSync.Models;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for validating person mappings and detecting issues
/// </summary>
public interface IMappingValidationService
{
    /// <summary>
    /// Validates a set of person mappings for consistency
    /// </summary>
    /// <param name="mappings">Person mappings to validate (source ID -> dest ID)</param>
    /// <param name="sourcePersons">Source persons dictionary</param>
    /// <param name="destPersons">Destination persons dictionary</param>
    /// <param name="sourceFamilies">Source families dictionary</param>
    /// <param name="destFamilies">Destination families dictionary</param>
    /// <returns>Validation result with any issues found</returns>
    ValidationResult ValidateMappings(
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, PersonRecord> sourcePersons,
        IReadOnlyDictionary<string, PersonRecord> destPersons,
        IReadOnlyDictionary<string, Family> sourceFamilies,
        IReadOnlyDictionary<string, Family> destFamilies);

    /// <summary>
    /// Removes mappings with high severity issues
    /// </summary>
    /// <param name="mappings">Original mappings</param>
    /// <param name="validation">Validation result</param>
    /// <param name="sourceFamilies">Source families for finding dependents</param>
    /// <returns>Cleaned mappings with suspicious entries removed</returns>
    Dictionary<string, string> RollbackSuspiciousMappings(
        Dictionary<string, string> mappings,
        ValidationResult validation,
        IReadOnlyDictionary<string, Family> sourceFamilies);

    /// <summary>
    /// Calculates confidence level for a mapping
    /// </summary>
    /// <param name="score">Match score (0-100)</param>
    /// <param name="matchedBy">Match method (RFN, Fuzzy, Family, etc.)</param>
    /// <returns>Confidence level (0.0 - 1.0)</returns>
    double CalculateConfidence(double score, string matchedBy);
}
