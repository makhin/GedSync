using System;
using System.Linq;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;

namespace GedcomGeniSync.Cli.Services;

public class ConfigurationService : IConfigurationService
{
    private static readonly string[] DefaultConfigPaths =
    {
        "gedsync.json",
        "gedsync.yaml",
        "gedsync.yml",
        ".gedsync.json",
        ".gedsync.yaml",
        ".gedsync.yml"
    };

    private readonly IConfigurationLoader _configurationLoader;

    public ConfigurationService(IConfigurationLoader configurationLoader)
    {
        _configurationLoader = configurationLoader;
    }

    public GedSyncConfiguration LoadConfiguration(string? configPath)
    {
        var candidatePaths = (configPath != null ? new[] { configPath } : Array.Empty<string>())
            .Concat(DefaultConfigPaths)
            .ToArray();

        return _configurationLoader.LoadWithDefaults(candidatePaths);
    }

    public CompareConfigurationSettings BuildCompareSettings(GedSyncConfiguration configuration, CompareOverrides overrides)
    {
        return new CompareConfigurationSettings(
            overrides.NewNodeDepth ?? configuration.Compare.NewNodeDepth,
            overrides.Threshold ?? configuration.Compare.MatchThreshold,
            overrides.IncludeDeletes ?? configuration.Compare.IncludeDeleteSuggestions,
            overrides.RequireUnique ?? configuration.Compare.RequireUniqueMatch,
            overrides.Verbose ?? configuration.Logging.Verbose);
    }

    public SyncConfigurationSettings BuildSyncSettings(GedSyncConfiguration configuration, SyncOverrides overrides)
    {
        return new SyncConfigurationSettings(
            overrides.DryRun ?? configuration.Sync.DryRun,
            overrides.Threshold ?? configuration.Matching.MatchThreshold,
            overrides.MaxDepth ?? configuration.Sync.MaxDepth,
            overrides.StateFile ?? configuration.Paths.StateFile,
            overrides.ReportFile ?? configuration.Paths.ReportFile,
            overrides.GivenNamesCsv ?? configuration.NameVariants.GivenNamesCsv,
            overrides.SurnamesCsv ?? configuration.NameVariants.SurnamesCsv,
            overrides.Verbose ?? configuration.Logging.Verbose,
            overrides.SyncPhotos ?? configuration.Sync.SyncPhotos);
    }
}
