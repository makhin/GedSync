using GedcomGeniSync.Models;

namespace GedcomGeniSync.Cli.Services;

public interface IConfigurationService
{
    GedSyncConfiguration LoadConfiguration(string? configPath);

    CompareConfigurationSettings BuildCompareSettings(GedSyncConfiguration configuration, CompareOverrides overrides);

    SyncConfigurationSettings BuildSyncSettings(GedSyncConfiguration configuration, SyncOverrides overrides);
}

public record CompareOverrides(int? NewNodeDepth, int? Threshold, bool? IncludeDeletes, bool? RequireUnique, bool? Verbose);

public record SyncOverrides(
    bool? DryRun,
    int? Threshold,
    int? MaxDepth,
    string? StateFile,
    string? ReportFile,
    string? GivenNamesCsv,
    string? SurnamesCsv,
    bool? Verbose,
    bool? SyncPhotos);

public record CompareConfigurationSettings(
    int NewNodeDepth,
    int Threshold,
    bool IncludeDeletes,
    bool RequireUnique,
    bool Verbose);

public record SyncConfigurationSettings(
    bool DryRun,
    int Threshold,
    int? MaxDepth,
    string? StateFile,
    string? ReportFile,
    string? GivenNamesCsv,
    string? SurnamesCsv,
    bool Verbose,
    bool SyncPhotos);
