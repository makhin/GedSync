using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Main orchestrator service for comparing two GEDCOM files
/// Coordinates individual and family comparison
/// </summary>
public interface IGedcomCompareService
{
    /// <summary>
    /// Compare two GEDCOM files and return comprehensive comparison result
    /// </summary>
    /// <param name="sourceFilePath">Path to source GEDCOM file (e.g., MyHeritage export)</param>
    /// <param name="destinationFilePath">Path to destination GEDCOM file (e.g., Geni export)</param>
    /// <param name="options">Comparison options</param>
    /// <returns>Complete comparison result with individuals and families</returns>
    CompareResult Compare(string sourceFilePath, string destinationFilePath, CompareOptions options);
}
