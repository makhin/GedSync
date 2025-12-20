using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

/// <summary>
/// Command handler for updating existing Geni profiles based on wave-compare results
/// </summary>
public class UpdateCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _inputOption = new("--input", description: "JSON file from wave-compare command") { IsRequired = true };
    private readonly Option<string> _gedcomOption = new("--gedcom", description: "MyHeritage GEDCOM file (for complete data)") { IsRequired = true };
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Geni API token file path");
    private readonly Option<bool> _dryRunOption = new("--dry-run", () => false, description: "Simulate updates without making changes");
    private readonly Option<bool> _verboseOption = new("--verbose", () => false, description: "Enable verbose logging");
    private readonly Option<bool> _syncPhotosOption = new("--sync-photos", () => true, description: "Synchronize photos from MyHeritage");
    private readonly Option<string?> _skipFieldsOption = new("--skip-fields", description: "Comma-separated list of fields to skip (e.g., BirthPlace,DeathPlace)");
    private readonly Option<bool> _resumeOption = new("--resume", () => false, description: "Resume from previous progress");

    public UpdateCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var updateCommand = new Command("update", "Update existing Geni profiles based on wave-compare results");

        updateCommand.AddOption(_inputOption);
        updateCommand.AddOption(_gedcomOption);
        updateCommand.AddOption(_tokenFileOption);
        updateCommand.AddOption(_dryRunOption);
        updateCommand.AddOption(_verboseOption);
        updateCommand.AddOption(_syncPhotosOption);
        updateCommand.AddOption(_skipFieldsOption);
        updateCommand.AddOption(_resumeOption);

        updateCommand.SetHandler(HandleAsync);
        return updateCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var inputPath = parseResult.GetValueForOption(_inputOption)!;
        var gedcomPath = parseResult.GetValueForOption(_gedcomOption)!;
        var tokenFile = parseResult.GetValueForOption(_tokenFileOption)!;
        var dryRun = parseResult.GetValueForOption(_dryRunOption);
        var verbose = parseResult.GetValueForOption(_verboseOption);
        var syncPhotos = parseResult.GetValueForOption(_syncPhotosOption);
        var skipFieldsStr = parseResult.GetValueForOption(_skipFieldsOption);
        var resume = parseResult.GetValueForOption(_resumeOption);

        // Parse skip fields
        var skipFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(skipFieldsStr))
        {
            foreach (var field in skipFieldsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                skipFields.Add(field);
            }
        }

        var accessToken = new Lazy<string>(() => AccessTokenResolver.ResolveFromFile(tokenFile));

        await using var scope = _startup.CreateScope(verbose, services =>
        {
            services.AddSingleton<WaveReportLoader>();

            services.AddSingleton<IGeniProfileClient>(sp =>
            {
                return new GedcomGeniSync.ApiClient.Services.GeniProfileClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    accessToken.Value,
                    dryRun,
                    sp.GetRequiredService<ILogger<GedcomGeniSync.ApiClient.Services.GeniProfileClient>>());
            });

            services.AddSingleton<IGeniPhotoClient>(sp =>
            {
                return new GedcomGeniSync.ApiClient.Services.GeniPhotoClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    accessToken.Value,
                    dryRun,
                    sp.GetRequiredService<ILogger<GedcomGeniSync.ApiClient.Services.GeniPhotoClient>>());
            });

            services.AddSingleton<IMyHeritagePhotoService>(sp =>
                new GedcomGeniSync.Services.MyHeritagePhotoService(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Services.MyHeritagePhotoService>>(),
                    dryRun));
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Update");

        try
        {
            logger.LogInformation("=== Update Command ===");
            logger.LogInformation("Input JSON: {Input}", inputPath);
            logger.LogInformation("GEDCOM: {Gedcom}", gedcomPath);
            logger.LogInformation("Dry Run: {DryRun}", dryRun);
            logger.LogInformation("Sync Photos: {SyncPhotos}", syncPhotos);
            if (skipFields.Count > 0)
            {
                logger.LogInformation("Skip Fields: {SkipFields}", string.Join(", ", skipFields));
            }

            // 1. Load and parse JSON report
            var reportLoader = provider.GetRequiredService<WaveReportLoader>();
            var report = await reportLoader.LoadAsync(inputPath);
            if (report == null)
            {
                context.ExitCode = 1;
                return;
            }

            logger.LogInformation("Report loaded: {UpdateCount} profiles to update",
                report.Individuals.NodesToUpdate.Count);

            // 2. Load GEDCOM file
            logger.LogInformation("Loading GEDCOM file...");
            var gedcomLoader = provider.GetRequiredService<IGedcomLoader>();
            var gedcomResult = gedcomLoader.Load(gedcomPath);

            logger.LogInformation("GEDCOM loaded: {Count} individuals", gedcomResult.Persons.Count);

            // 3. Check for existing progress
            var progressTracker = new ProgressTracker(provider.GetRequiredService<ILogger<ProgressTracker>>());
            UpdateProgress? existingProgress = null;

            if (resume)
            {
                existingProgress = progressTracker.LoadUpdateProgress(inputPath);
                if (existingProgress != null)
                {
                    logger.LogInformation("Resuming from previous progress...");
                    logger.LogInformation("Previous run: {Processed}/{Total} profiles processed",
                        existingProgress.ProcessedSourceIds.Count, existingProgress.TotalProfiles);
                }
                else
                {
                    logger.LogWarning("No previous progress found. Starting from beginning.");
                }
            }
            else
            {
                // Check if there's existing progress and warn user
                var hasProgress = progressTracker.LoadUpdateProgress(inputPath) != null;
                if (hasProgress)
                {
                    logger.LogWarning("Found existing progress file. Use --resume to continue from where you left off, or this will start from the beginning.");
                }
            }

            // 4. Execute updates
            var profileClient = provider.GetRequiredService<IGeniProfileClient>();
            var photoClient = provider.GetRequiredService<IGeniPhotoClient>();
            var photoService = provider.GetRequiredService<IMyHeritagePhotoService>();

            var updateService = new UpdateExecutor(
                profileClient,
                photoClient,
                photoService,
                gedcomResult,
                logger,
                progressTracker,
                inputPath);

            var updateProgress = existingProgress ?? new UpdateProgress
            {
                InputFile = inputPath,
                GedcomFile = gedcomPath,
                TotalProfiles = report.Individuals.NodesToUpdate.Count
            };

            var result = await updateService.ExecuteUpdatesAsync(
                report.Individuals.NodesToUpdate,
                skipFields,
                syncPhotos,
                updateProgress);

            // 4. Display results
            logger.LogInformation("");
            logger.LogInformation("=== Update Results ===");
            logger.LogInformation("Total Processed: {Total}", result.TotalProcessed);
            logger.LogInformation("Successful: {Success}", result.Successful);
            logger.LogInformation("Failed: {Failed}", result.Failed);

            if (syncPhotos)
            {
                logger.LogInformation("Photos Uploaded: {PhotosUploaded}", result.PhotosUploaded);
                logger.LogInformation("Photos Failed: {PhotosFailed}", result.PhotosFailed);
            }

            if (result.Errors.Count > 0)
            {
                logger.LogWarning("");
                logger.LogWarning("Errors:");
                foreach (var error in result.Errors)
                {
                    logger.LogWarning("  [{SourceId}] {Field}: {Error}",
                        error.SourceId, error.FieldName, error.ErrorMessage);
                }
            }

            context.ExitCode = result.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update command failed");
            context.ExitCode = 1;
        }
    }
}

/// <summary>
/// Result of update operation
/// </summary>
public class UpdateResult
{
    public int TotalProcessed { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public int PhotosUploaded { get; set; }
    public int PhotosFailed { get; set; }
    public List<UpdateError> Errors { get; set; } = new();
}

/// <summary>
/// Error that occurred during update
/// </summary>
public class UpdateError
{
    public required string SourceId { get; set; }
    public required string GeniProfileId { get; set; }
    public required string FieldName { get; set; }
    public required string ErrorMessage { get; set; }
}
