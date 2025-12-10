namespace GedcomGeniSync.Services;

/// <summary>
/// Manages synchronization state including ID mappings and processed records
/// </summary>
public interface ISyncStateManager
{
    /// <summary>
    /// Check if a GEDCOM ID has already been processed
    /// </summary>
    bool IsProcessed(string gedcomId);

    /// <summary>
    /// Mark a GEDCOM ID as processed
    /// </summary>
    void MarkAsProcessed(string gedcomId);

    /// <summary>
    /// Add a bidirectional mapping between GEDCOM and Geni IDs
    /// </summary>
    void AddMapping(string gedcomId, string geniId);

    /// <summary>
    /// Get Geni ID for a GEDCOM ID
    /// </summary>
    string? GetGeniId(string gedcomId);

    /// <summary>
    /// Get GEDCOM ID for a Geni ID
    /// </summary>
    string? GetGedcomId(string geniId);

    /// <summary>
    /// Check if a Geni ID is already mapped
    /// </summary>
    bool IsMappedToGeni(string geniId);

    /// <summary>
    /// Get all GEDCOM to Geni mappings
    /// </summary>
    IReadOnlyDictionary<string, string> GetAllMappings();

    /// <summary>
    /// Load mappings and processed IDs from collections
    /// </summary>
    void LoadState(Dictionary<string, string> gedcomToGeniMap, List<string> processedIds);

    /// <summary>
    /// Get all processed GEDCOM IDs
    /// </summary>
    IReadOnlyCollection<string> GetProcessedIds();

    /// <summary>
    /// Clear all state
    /// </summary>
    void Clear();
}
