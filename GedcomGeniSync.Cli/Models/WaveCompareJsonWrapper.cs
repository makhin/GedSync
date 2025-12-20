using GedcomGeniSync.Core.Models.Wave;

namespace GedcomGeniSync.Cli.Models;

/// <summary>
/// Wrapper for wave-compare JSON output that includes summary, report, and wave result
/// </summary>
public record WaveCompareJsonWrapper
{
    public WaveHighConfidenceReport? Report { get; init; }
}
