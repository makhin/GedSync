namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for synchronization service
/// Orchestrates synchronization from GEDCOM to Geni
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Run synchronization from GEDCOM file to Geni
    /// </summary>
    /// <param name="gedcomPath">Path to GEDCOM file</param>
    /// <param name="anchorGedcomId">GEDCOM ID of the anchor person</param>
    /// <param name="anchorGeniId">Geni ID of the anchor person</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Synchronization report with statistics and results</returns>
    Task<SyncReport> SyncAsync(
        string gedcomPath,
        string anchorGedcomId,
        string anchorGeniId,
        CancellationToken cancellationToken = default);
}
