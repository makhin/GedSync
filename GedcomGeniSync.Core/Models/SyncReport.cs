using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

[ExcludeFromCodeCoverage]
public class SyncReport
{
    public int TotalProcessed { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<SyncResult> Results { get; set; } = new();
    public Dictionary<string, string> GedcomToGeniMap { get; set; } = new();
    public SyncStatistics? Statistics { get; set; }

    public void PrintSummary(ILogger logger)
    {
        logger.LogInformation("=== Sync Report ===");
        logger.LogInformation("Total processed: {Total}", TotalProcessed);
        logger.LogInformation("Matched: {Count} ({Percent:P0})", Matched, (double)Matched / TotalProcessed);
        logger.LogInformation("Created: {Count} ({Percent:P0})", Created, (double)Created / TotalProcessed);
        logger.LogInformation("Skipped: {Count} ({Percent:P0})", Skipped, (double)Skipped / TotalProcessed);
        logger.LogInformation("Errors: {Count} ({Percent:P0})", Errors, (double)Errors / TotalProcessed);

        Statistics?.LogSummary(logger);
    }

    public void PrintDetails(ILogger logger)
    {
        logger.LogInformation("=== Detailed Results ===");

        foreach (var result in Results)
        {
            var status = result.Action switch
            {
                SyncAction.Matched => $"MATCHED (score: {result.MatchScore}%)",
                SyncAction.Created => $"CREATED as {result.RelationType}",
                SyncAction.Skipped => $"SKIPPED: {result.ErrorMessage}",
                SyncAction.Error => $"ERROR: {result.ErrorMessage}",
                _ => "UNKNOWN"
            };

            var geniLink = !string.IsNullOrEmpty(result.GeniId)
                ? $" → https://www.geni.com/people/{result.GeniId}"
                : string.Empty;

            logger.LogInformation("{GedId}: {Name} - {Status}{Link}",
                result.GedcomId, result.PersonName, status, geniLink);
        }
    }

    public async Task SaveToFileAsync(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
