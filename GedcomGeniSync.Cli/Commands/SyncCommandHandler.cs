using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class SyncCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string?> _configOption = new("--config", description: "Path to configuration file (JSON or YAML)");
    private readonly Option<string> _gedcomOption = new("--gedcom", description: "Path to GEDCOM file") { IsRequired = true };
    private readonly Option<string> _anchorGedOption = new("--anchor-ged", description: "GEDCOM ID of anchor person (e.g., @I123@)") { IsRequired = true };
    private readonly Option<string> _anchorGeniOption = new("--anchor-geni", description: "Geni profile ID of anchor person") { IsRequired = true };
    private readonly Option<string?> _tokenOption = new("--token", description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Path to saved token file from auth command");
    private readonly Option<bool?> _dryRunOption = new("--dry-run", description: "Preview changes without creating profiles");
    private readonly Option<int?> _thresholdOption = new("--threshold", description: "Match threshold (0-100)");
    private readonly Option<int?> _maxDepthOption = new("--max-depth", description: "Maximum BFS depth (null for unlimited)");
    private readonly Option<string?> _stateFileOption = new("--state-file", description: "Path to state file for resume support");
    private readonly Option<string?> _reportFileOption = new("--report", description: "Path to save report");
    private readonly Option<string?> _givenNamesOption = new("--given-names-csv", description: "Path to given names variants CSV");
    private readonly Option<string?> _surnamesOption = new("--surnames-csv", description: "Path to surnames variants CSV");
    private readonly Option<bool?> _verboseOption = new("--verbose", description: "Enable verbose logging");
    private readonly Option<bool?> _syncPhotosOption = new("--sync-photos", description: "Enable photo synchronization from MyHeritage");

    public SyncCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var syncCommand = new Command("sync", "Synchronize GEDCOM file to Geni");

        syncCommand.AddOption(_configOption);
        syncCommand.AddOption(_gedcomOption);
        syncCommand.AddOption(_anchorGedOption);
        syncCommand.AddOption(_anchorGeniOption);
        syncCommand.AddOption(_tokenOption);
        syncCommand.AddOption(_tokenFileOption);
        syncCommand.AddOption(_dryRunOption);
        syncCommand.AddOption(_thresholdOption);
        syncCommand.AddOption(_maxDepthOption);
        syncCommand.AddOption(_stateFileOption);
        syncCommand.AddOption(_reportFileOption);
        syncCommand.AddOption(_givenNamesOption);
        syncCommand.AddOption(_surnamesOption);
        syncCommand.AddOption(_verboseOption);
        syncCommand.AddOption(_syncPhotosOption);

        syncCommand.SetHandler(HandleAsync);
        return syncCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var configPath = parseResult.GetValueForOption(_configOption);
        var gedcomPath = parseResult.GetValueForOption(_gedcomOption)!;
        var anchorGed = parseResult.GetValueForOption(_anchorGedOption)!;
        var anchorGeni = parseResult.GetValueForOption(_anchorGeniOption)!;
        var token = parseResult.GetValueForOption(_tokenOption);
        var tokenFile = parseResult.GetValueForOption(_tokenFileOption)!;

        var overrides = new SyncOverrides(
            parseResult.GetValueForOption(_dryRunOption),
            parseResult.GetValueForOption(_thresholdOption),
            parseResult.GetValueForOption(_maxDepthOption),
            parseResult.GetValueForOption(_stateFileOption),
            parseResult.GetValueForOption(_reportFileOption),
            parseResult.GetValueForOption(_givenNamesOption),
            parseResult.GetValueForOption(_surnamesOption),
            parseResult.GetValueForOption(_verboseOption),
            parseResult.GetValueForOption(_syncPhotosOption));

        using var configScope = _startup.CreateScope(overrides.Verbose ?? false);
        var configurationService = configScope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var configuration = configurationService.LoadConfiguration(configPath);
        var settings = configurationService.BuildSyncSettings(configuration, overrides);

        token ??= Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");

        GeniAuthToken? storedToken = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            storedToken = await GeniAuthClient.LoadTokenFromFileAsync(tokenFile);

            if (storedToken != null && !storedToken.IsExpired)
            {
                token = storedToken.AccessToken;
            }
        }

        var matchingOptions = configuration.Matching.ToMatchingOptions() with { MatchThreshold = settings.Threshold };

        await using var scope = _startup.CreateScope(settings.Verbose, services =>
        {
            services.AddSingleton(matchingOptions);
            services.AddSingleton(sp => new SyncOptions
            {
                StateFilePath = settings.StateFile,
                MaxDepth = settings.MaxDepth,
                MatchingOptions = sp.GetRequiredService<MatchingOptions>(),
                SyncPhotos = settings.SyncPhotos,
                DryRun = settings.DryRun
            });
            services.AddSingleton<IFuzzyMatcherService>(sp => new FuzzyMatcherService(
                sp.GetRequiredService<INameVariantsService>(),
                sp.GetRequiredService<ILogger<FuzzyMatcherService>>(),
                sp.GetRequiredService<MatchingOptions>()));
            services.AddSingleton<IGeniProfileClient>(sp => new GeniProfileClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                token ?? string.Empty,
                settings.DryRun,
                sp.GetRequiredService<ILogger<GeniProfileClient>>()));
            services.AddSingleton<IGeniPhotoClient>(sp => new GeniPhotoClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                token ?? string.Empty,
                settings.DryRun,
                sp.GetRequiredService<ILogger<GeniPhotoClient>>()));
            services.AddSingleton<IGeniApiClient>(sp => new GeniApiClient(
                sp.GetRequiredService<IGeniProfileClient>(),
                sp.GetRequiredService<IGeniPhotoClient>()));
            services.AddSingleton<IMyHeritagePhotoService>(sp => new MyHeritagePhotoService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<MyHeritagePhotoService>>(),
                settings.DryRun));
            services.AddSingleton<ISyncStateManager, SyncStateManager>();
            services.AddSingleton<ISyncService>(sp => new SyncService(
                sp.GetRequiredService<IGedcomLoader>(),
                sp.GetRequiredService<IGeniApiClient>(),
                sp.GetRequiredService<IFuzzyMatcherService>(),
                sp.GetRequiredService<ISyncStateManager>(),
                sp.GetRequiredService<ILogger<SyncService>>(),
                sp.GetRequiredService<SyncOptions>(),
                settings.SyncPhotos ? sp.GetRequiredService<IMyHeritagePhotoService>() : null));
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sync");

        try
        {
            logger.LogInformation("Configuration source: {Config}", string.IsNullOrEmpty(configPath) ? "auto-detected defaults" : configPath);
            if (storedToken != null)
            {
                if (storedToken.IsExpired)
                {
                    logger.LogError("Stored token at {Path} is expired. Run the auth command to refresh it.", tokenFile);
                }
                else
                {
                    logger.LogInformation("Using stored token from {Path}", tokenFile);
                }
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogError("Geni access token required. Use --token, set GENI_ACCESS_TOKEN, or load from --token-file.");
                context.ExitCode = 1;
                return;
            }

            var nameVariants = provider.GetRequiredService<INameVariantsService>();
            if (!string.IsNullOrEmpty(settings.GivenNamesCsv) || !string.IsNullOrEmpty(settings.SurnamesCsv))
            {
                nameVariants.LoadFromCsv(settings.GivenNamesCsv ?? string.Empty, settings.SurnamesCsv ?? string.Empty);
            }

            logger.LogInformation("=== GEDCOM to Geni Sync ===");
            logger.LogInformation("Mode: {Mode}", settings.DryRun ? "DRY-RUN (no changes)" : "LIVE");
            logger.LogInformation("Match threshold: {Threshold}%", settings.Threshold);
            logger.LogInformation("Photo sync: {Status}", settings.SyncPhotos ? "ENABLED" : "DISABLED");
            logger.LogInformation("Max depth: {Depth}", settings.MaxDepth?.ToString() ?? "unlimited");
            logger.LogInformation("State file: {StateFile} | Report file: {ReportFile}",
                settings.StateFile ?? "<not set>", settings.ReportFile ?? "<not set>");
            logger.LogInformation("Name variants: given={GivenNames}, surnames={Surnames}",
                settings.GivenNamesCsv ?? "<none>", settings.SurnamesCsv ?? "<none>");

            var syncService = provider.GetRequiredService<ISyncService>();
            var report = await syncService.SyncAsync(
                gedcomPath, anchorGed, anchorGeni, context.GetCancellationToken());

            report.PrintSummary(logger);

            if (settings.Verbose)
            {
                report.PrintDetails(logger);
            }

            if (!string.IsNullOrEmpty(settings.ReportFile))
            {
                await report.SaveToFileAsync(settings.ReportFile);
                logger.LogInformation("Report saved to: {Path}", settings.ReportFile);
            }

            context.ExitCode = report.Errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed");
            context.ExitCode = 1;
        }
    }
}
