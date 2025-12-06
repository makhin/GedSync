using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for GEDCOM file loader
/// Loads and parses GEDCOM files into PersonRecord models
/// </summary>
public interface IGedcomLoader
{
    /// <summary>
    /// Load GEDCOM file and return dictionary of PersonRecords keyed by GEDCOM ID
    /// </summary>
    GedcomLoadResult Load(string filePath);

    /// <summary>
    /// Find person by GEDCOM ID
    /// </summary>
    PersonRecord? FindById(GedcomLoadResult result, string gedcomId);

    /// <summary>
    /// Get all relatives of a person (for BFS traversal)
    /// </summary>
    IEnumerable<string> GetRelativeIds(PersonRecord person);
}
