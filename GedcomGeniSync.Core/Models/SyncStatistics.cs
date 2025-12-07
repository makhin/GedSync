using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Runtime statistics for a synchronization run.
/// </summary>
[ExcludeFromCodeCoverage]
public class SyncStatistics
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int GedcomPersonsTotal { get; set; }
    public int GedcomFamiliesTotal { get; set; }
    public int QueueEnqueued { get; set; }
    public int QueueDequeued { get; set; }
    public int MatchAttempts { get; set; }
    public int GeniFamilyRequests { get; set; }
    public int ProfilesCreated { get; set; }
    public int ProfilesMatched { get; set; }
    public int ProfilesSkipped { get; set; }
    public int ProfileErrors { get; set; }
    public int PhotoDownloadAttempts { get; set; }
    public int PhotoUploads { get; set; }
    public int DryRunProfileCreations { get; set; }

    public TimeSpan Duration => (FinishedAt ?? DateTime.UtcNow) - StartedAt;

    public SyncStatistics Clone()
    {
        return (SyncStatistics)MemberwiseClone();
    }

    public void LogSummary(ILogger logger)
    {
        logger.LogInformation("=== Runtime Statistics ===");
        logger.LogInformation("Duration: {Duration:c}", Duration);
        logger.LogInformation("GEDCOM totals: {Persons} persons, {Families} families", GedcomPersonsTotal, GedcomFamiliesTotal);
        logger.LogInformation("Queue: {Enqueued} enqueued / {Dequeued} processed", QueueEnqueued, QueueDequeued);
        logger.LogInformation("Matching attempts: {Attempts}, Geni family lookups: {Lookups}", MatchAttempts, GeniFamilyRequests);
        logger.LogInformation("Profiles matched: {Matched}, created: {Created}, skipped: {Skipped}, errors: {Errors}",
            ProfilesMatched, ProfilesCreated, ProfilesSkipped, ProfileErrors);
        logger.LogInformation("Photos: {Downloads} download attempts, {Uploads} uploads", PhotoDownloadAttempts, PhotoUploads);
        if (DryRunProfileCreations > 0)
        {
            logger.LogInformation("Dry-run profile creations: {Count}", DryRunProfileCreations);
        }
    }
}
