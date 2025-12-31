using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Services.NameFix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

/// <summary>
/// Command handler for adding new profiles to Geni based on wave-compare results
/// </summary>
public class AddCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _inputOption = new("--input", description: "JSON file from wave-compare command") { IsRequired = true };
    private readonly Option<string> _gedcomOption = new("--gedcom", description: "MyHeritage GEDCOM file (for complete data)") { IsRequired = true };
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Geni API token file path");
    private readonly Option<bool> _dryRunOption = new("--dry-run", () => false, description: "Simulate additions without making changes");
    private readonly Option<bool> _verboseOption = new("--verbose", () => false, description: "Enable verbose logging");
    private readonly Option<bool> _syncPhotosOption = new("--sync-photos", () => true, description: "Upload photos from MyHeritage");
    private readonly Option<int> _maxDepthOption = new("--max-depth", () => int.MaxValue, description: "Maximum depth from existing nodes to add");
    private readonly Option<bool> _resumeOption = new("--resume", () => false, description: "Resume from previous progress");
    private readonly Option<bool> _interactiveOption = new("--interactive", () => false, description: "Prompt for confirmation before each add/update operation");

    public AddCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var addCommand = new Command("add", "Add new profiles to Geni based on wave-compare results");

        addCommand.AddOption(_inputOption);
        addCommand.AddOption(_gedcomOption);
        addCommand.AddOption(_tokenFileOption);
        addCommand.AddOption(_dryRunOption);
        addCommand.AddOption(_verboseOption);
        addCommand.AddOption(_syncPhotosOption);
        addCommand.AddOption(_maxDepthOption);
        addCommand.AddOption(_resumeOption);
        addCommand.AddOption(_interactiveOption);

        addCommand.SetHandler(HandleAsync);
        return addCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var inputPath = parseResult.GetValueForOption(_inputOption)!;
        var gedcomPath = parseResult.GetValueForOption(_gedcomOption)!;
        var tokenFile = parseResult.GetValueForOption(_tokenFileOption)!;
        var dryRun = parseResult.GetValueForOption(_dryRunOption);
        var resume = parseResult.GetValueForOption(_resumeOption);
        var verbose = parseResult.GetValueForOption(_verboseOption);
        var syncPhotos = parseResult.GetValueForOption(_syncPhotosOption);
        var maxDepth = parseResult.GetValueForOption(_maxDepthOption);
        var interactive = parseResult.GetValueForOption(_interactiveOption);

        var accessToken = new Lazy<string>(() => AccessTokenResolver.ResolveFromFile(tokenFile));

        await using var scope = _startup.CreateScope(verbose, services =>
        {
            services.AddSingleton<WaveReportLoader>();

            services.AddSingleton<InteractiveConfirmationService>(sp =>
                new InteractiveConfirmationService(
                    interactive,
                    sp.GetRequiredService<ILogger<InteractiveConfirmationService>>()));

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

            services.AddSingleton<IPhotoDownloadService>(sp =>
                new GedcomGeniSync.Services.PhotoDownloadService(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Services.PhotoDownloadService>>(),
                    dryRun));

            // Register name fix pipeline for fixing names before profile creation
            NameFixPipelineFactory.RegisterNameFixServices(services);
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Add");

        try
        {
            logger.LogInformation("=== Add Command ===");
            logger.LogInformation("Input JSON: {Input}", inputPath);
            logger.LogInformation("GEDCOM: {Gedcom}", gedcomPath);
            logger.LogInformation("Dry Run: {DryRun}", dryRun);
            logger.LogInformation("Sync Photos: {SyncPhotos}", syncPhotos);
            logger.LogInformation("Max Depth: {MaxDepth}", maxDepth == int.MaxValue ? "unlimited" : maxDepth.ToString());

            // 1. Load and parse JSON report
            var reportLoader = provider.GetRequiredService<WaveReportLoader>();
            var report = await reportLoader.LoadAsync(inputPath);
            if (report == null)
            {
                context.ExitCode = 1;
                return;
            }

            logger.LogInformation("Report loaded: {AddCount} profiles to add",
                report.Individuals.NodesToAdd.Count);

            // Filter by max depth
            var nodesToAdd = report.Individuals.NodesToAdd
                .Where(n => n.DepthFromExisting <= maxDepth)
                .ToList();

            if (nodesToAdd.Count < report.Individuals.NodesToAdd.Count)
            {
                logger.LogInformation("Filtered to {FilteredCount} profiles within depth {MaxDepth}",
                    nodesToAdd.Count, maxDepth);
            }

            // 2. Load GEDCOM file
            logger.LogInformation("Loading GEDCOM file...");
            var gedcomLoader = provider.GetRequiredService<IGedcomLoader>();
            var gedcomResult = await gedcomLoader.LoadAsync(gedcomPath, downloadPhotos: syncPhotos);

            logger.LogInformation("GEDCOM loaded: {Count} individuals", gedcomResult.Persons.Count);

            // 3. Check for existing progress
            var progressTracker = new ProgressTracker(provider.GetRequiredService<ILogger<ProgressTracker>>());
            AddProgress? existingProgress = null;

            if (resume)
            {
                existingProgress = progressTracker.LoadAddProgress(inputPath);
                if (existingProgress != null)
                {
                    logger.LogInformation("Resuming from previous progress...");
                    logger.LogInformation("Previous run: {Processed}/{Total} profiles processed, {Created} created",
                        existingProgress.ProcessedSourceIds.Count, existingProgress.TotalProfiles,
                        existingProgress.CreatedProfiles.Count);
                }
                else
                {
                    logger.LogWarning("No previous progress found. Starting from beginning.");
                }
            }
            else
            {
                // Check if there's existing progress and warn user
                var hasProgress = progressTracker.LoadAddProgress(inputPath) != null;
                if (hasProgress)
                {
                    logger.LogWarning("Found existing progress file. Use --resume to continue from where you left off, or this will start from the beginning.");
                }
            }

            // 4. Execute additions
            var profileClient = provider.GetRequiredService<IGeniProfileClient>();
            var photoClient = provider.GetRequiredService<IGeniPhotoClient>();
            var photoService = provider.GetRequiredService<IPhotoDownloadService>();
            var confirmationService = provider.GetRequiredService<InteractiveConfirmationService>();

            var addService = new AddExecutor(
                profileClient,
                photoClient,
                photoService,
                provider.GetService<GedcomGeniSync.Services.Photo.IPhotoCacheService>(),
                provider.GetService<INameFixPipeline>(),
                gedcomResult,
                logger,
                confirmationService,
                progressTracker,
                inputPath);

            var addProgress = existingProgress ?? new AddProgress
            {
                InputFile = inputPath,
                GedcomFile = gedcomPath,
                TotalProfiles = nodesToAdd.Count
            };

            var result = await addService.ExecuteAdditionsAsync(
                nodesToAdd,
                report.Individuals.NodesToUpdate,
                syncPhotos,
                addProgress);

            // 4. Display results
            logger.LogInformation("");
            logger.LogInformation("=== Add Results ===");
            logger.LogInformation("Total Processed: {Total}", result.TotalProcessed);
            logger.LogInformation("Successful: {Success}", result.Successful);
            logger.LogInformation("Failed: {Failed}", result.Failed);
            logger.LogInformation("Skipped (no relation): {Skipped}", result.Skipped);

            if (syncPhotos)
            {
                logger.LogInformation("Photos Uploaded: {PhotosUploaded}", result.PhotosUploaded);
                logger.LogInformation("Photos Failed: {PhotosFailed}", result.PhotosFailed);
            }

            if (result.Errors.Count > 0)
            {
                logger.LogWarning("");
                logger.LogWarning("Errors:");
                foreach (var error in result.Errors.Take(20))
                {
                    logger.LogWarning("  [{SourceId}] {Relation}: {Error}",
                        error.SourceId, error.RelationType, error.ErrorMessage);
                }

                if (result.Errors.Count > 20)
                {
                    logger.LogWarning("  ... and {More} more errors", result.Errors.Count - 20);
                }
            }

            context.ExitCode = result.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Add command failed");
            context.ExitCode = 1;
        }
    }
}

/// <summary>
/// Result of add operation
/// </summary>
public class AddResult
{
    public int TotalProcessed { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int PhotosUploaded { get; set; }
    public int PhotosFailed { get; set; }
    public List<AddError> Errors { get; set; } = new();
    public Dictionary<string, string> CreatedProfiles { get; set; } = new(); // SourceId -> GeniProfileId

    /// <summary>
    /// Issues found during family relations validation (Phase 3)
    /// These require manual correction in Geni
    /// </summary>
    public List<RelationIssue> RelationIssues { get; set; } = new();
}

/// <summary>
/// Error that occurred during add
/// </summary>
public class AddError
{
    public required string SourceId { get; set; }
    public required string RelationType { get; set; }
    public required string ErrorMessage { get; set; }
}

/// <summary>
/// Issue with family relation that requires manual correction
/// </summary>
public class RelationIssue
{
    /// <summary>
    /// Type of relation issue
    /// </summary>
    public required RelationIssueType Type { get; set; }

    /// <summary>
    /// Source ID of the profile with the issue
    /// </summary>
    public required string SourceId { get; set; }

    /// <summary>
    /// Geni ID of the created profile
    /// </summary>
    public string? GeniId { get; set; }

    /// <summary>
    /// Related profile source ID
    /// </summary>
    public string? RelatedSourceId { get; set; }

    /// <summary>
    /// Related profile Geni ID
    /// </summary>
    public string? RelatedGeniId { get; set; }

    /// <summary>
    /// Description of the issue
    /// </summary>
    public required string Description { get; set; }
}

/// <summary>
/// Type of family relation issue
/// </summary>
public enum RelationIssueType
{
    /// <summary>
    /// Child is missing connection to second parent
    /// </summary>
    MissingParentLink,

    /// <summary>
    /// Two parents are not linked as spouses
    /// </summary>
    MissingSpouseLink,

    /// <summary>
    /// Partner is missing connection to existing children
    /// </summary>
    MissingChildLink
}
