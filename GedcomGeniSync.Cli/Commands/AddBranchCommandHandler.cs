using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

/// <summary>
/// Command handler for adding a branch of relatives from GEDCOM to Geni.
///
/// This command takes a starting person (start-id) from GEDCOM and creates them
/// along with all their descendants/relatives in Geni, linking them to an existing
/// anchor profile.
///
/// Example usage:
/// add-branch --source family.ged --anchor-source @I1@ --anchor-dest g123456789 --start-id @I5@
///
/// This will:
/// 1. Find person @I5@ in the GEDCOM file
/// 2. Determine their relationship to @I1@ (the anchor in GEDCOM)
/// 3. Create @I5@ in Geni and link to g123456789 (anchor in Geni)
/// 4. Recursively create all relatives of @I5@ (spouse, children, grandchildren, etc.)
/// </summary>
public class AddBranchCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _sourceOption = new("--source", description: "GEDCOM file path") { IsRequired = true };
    private readonly Option<string> _anchorSourceOption = new("--anchor-source", description: "Anchor person ID in GEDCOM (exists in both trees)") { IsRequired = true };
    private readonly Option<string> _anchorDestOption = new("--anchor-dest", description: "Anchor profile ID in Geni (same person as anchor-source)") { IsRequired = true };
    private readonly Option<string> _startIdOption = new("--start-id", description: "Starting person ID in GEDCOM (to be created and linked to anchor)") { IsRequired = true };
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Geni API token file path");
    private readonly Option<bool> _dryRunOption = new("--dry-run", () => false, description: "Simulate additions without making changes");
    private readonly Option<bool> _verboseOption = new("--verbose", () => false, description: "Enable verbose logging");
    private readonly Option<bool> _syncPhotosOption = new("--sync-photos", () => true, description: "Upload photos from GEDCOM");
    private readonly Option<int> _maxDepthOption = new("--max-depth", () => int.MaxValue, description: "Maximum depth from start-id to traverse");
    private readonly Option<bool> _interactiveOption = new("--interactive", () => false, description: "Prompt for confirmation before each add operation");

    public AddBranchCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var command = new Command("add-branch", "Add a branch of relatives from GEDCOM to Geni starting from a specific person");

        command.AddOption(_sourceOption);
        command.AddOption(_anchorSourceOption);
        command.AddOption(_anchorDestOption);
        command.AddOption(_startIdOption);
        command.AddOption(_tokenFileOption);
        command.AddOption(_dryRunOption);
        command.AddOption(_verboseOption);
        command.AddOption(_syncPhotosOption);
        command.AddOption(_maxDepthOption);
        command.AddOption(_interactiveOption);

        command.SetHandler(HandleAsync);
        return command;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var sourcePath = parseResult.GetValueForOption(_sourceOption)!;
        var anchorSource = parseResult.GetValueForOption(_anchorSourceOption)!;
        var anchorDest = parseResult.GetValueForOption(_anchorDestOption)!;
        var startId = parseResult.GetValueForOption(_startIdOption)!;
        var tokenFile = parseResult.GetValueForOption(_tokenFileOption)!;
        var dryRun = parseResult.GetValueForOption(_dryRunOption);
        var verbose = parseResult.GetValueForOption(_verboseOption);
        var syncPhotos = parseResult.GetValueForOption(_syncPhotosOption);
        var maxDepth = parseResult.GetValueForOption(_maxDepthOption);
        var interactive = parseResult.GetValueForOption(_interactiveOption);

        // Normalize GEDCOM IDs - they come escaped from command line preprocessing
        anchorSource = NormalizeGedcomId(anchorSource);
        startId = NormalizeGedcomId(startId);

        var accessToken = new Lazy<string>(() => AccessTokenResolver.ResolveFromFile(tokenFile));

        await using var scope = _startup.CreateScope(verbose, services =>
        {
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
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("AddBranch");

        try
        {
            logger.LogInformation("=== Add Branch Command ===");
            logger.LogInformation("GEDCOM Source: {Source}", sourcePath);
            logger.LogInformation("Anchor (GEDCOM): {AnchorSource}", anchorSource);
            logger.LogInformation("Anchor (Geni): {AnchorDest}", anchorDest);
            logger.LogInformation("Start ID: {StartId}", startId);
            logger.LogInformation("Dry Run: {DryRun}", dryRun);
            logger.LogInformation("Sync Photos: {SyncPhotos}", syncPhotos);
            logger.LogInformation("Max Depth: {MaxDepth}", maxDepth == int.MaxValue ? "unlimited" : maxDepth.ToString());

            // 1. Load GEDCOM file
            logger.LogInformation("Loading GEDCOM file...");
            var gedcomLoader = provider.GetRequiredService<IGedcomLoader>();
            var gedcomResult = await gedcomLoader.LoadAsync(sourcePath, downloadPhotos: syncPhotos);

            logger.LogInformation("GEDCOM loaded: {Count} individuals", gedcomResult.Persons.Count);

            // 2. Validate anchor and start-id exist
            if (!gedcomResult.Persons.TryGetValue(anchorSource, out var anchorPerson))
            {
                logger.LogError("Anchor person {AnchorSource} not found in GEDCOM file", anchorSource);
                context.ExitCode = 1;
                return;
            }

            if (!gedcomResult.Persons.TryGetValue(startId, out var startPerson))
            {
                logger.LogError("Start person {StartId} not found in GEDCOM file", startId);
                context.ExitCode = 1;
                return;
            }

            logger.LogInformation("Anchor person: {Name}", anchorPerson.FullName);
            logger.LogInformation("Start person: {Name}", startPerson.FullName);

            // 3. Determine relationship between start-id and anchor
            var relationship = DetermineRelationship(startPerson, anchorSource);
            if (relationship == null)
            {
                logger.LogError("Start person {StartId} is not directly related to anchor {AnchorSource}",
                    startId, anchorSource);
                logger.LogError("The start-id must be a child, parent, or spouse of the anchor-source");
                context.ExitCode = 1;
                return;
            }

            logger.LogInformation("Relationship: {StartId} is {Relationship} of {AnchorSource}",
                startId, relationship, anchorSource);

            // 4. Create the branch executor and process
            var profileClient = provider.GetRequiredService<IGeniProfileClient>();
            var photoClient = provider.GetRequiredService<IGeniPhotoClient>();
            var photoService = provider.GetRequiredService<IPhotoDownloadService>();
            var confirmationService = provider.GetRequiredService<InteractiveConfirmationService>();

            var executor = new AddBranchExecutor(
                profileClient,
                photoClient,
                photoService,
                gedcomResult,
                logger,
                confirmationService);

            var result = await executor.ExecuteAsync(
                anchorSource,
                anchorDest,
                startId,
                relationship.Value,
                syncPhotos,
                maxDepth);

            // 5. Display results
            logger.LogInformation("");
            logger.LogInformation("=== Add Branch Results ===");
            logger.LogInformation("Total Processed: {Total}", result.TotalProcessed);
            logger.LogInformation("Created: {Created}", result.Created);
            logger.LogInformation("Failed: {Failed}", result.Failed);
            logger.LogInformation("Skipped: {Skipped}", result.Skipped);

            if (syncPhotos)
            {
                logger.LogInformation("Photos Uploaded: {PhotosUploaded}", result.PhotosUploaded);
                logger.LogInformation("Photos Failed: {PhotosFailed}", result.PhotosFailed);
            }

            if (result.CreatedProfiles.Count > 0)
            {
                logger.LogInformation("");
                logger.LogInformation("Created Profiles:");
                foreach (var (sourceId, geniId) in result.CreatedProfiles)
                {
                    var person = gedcomResult.Persons.GetValueOrDefault(sourceId);
                    logger.LogInformation("  {Name} ({SourceId}) -> {GeniId}",
                        person?.FullName ?? sourceId, sourceId, geniId);
                }
            }

            if (result.Errors.Count > 0)
            {
                logger.LogWarning("");
                logger.LogWarning("Errors:");
                foreach (var error in result.Errors.Take(20))
                {
                    logger.LogWarning("  [{SourceId}]: {Error}", error.SourceId, error.ErrorMessage);
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
            logger.LogError(ex, "Add branch command failed");
            context.ExitCode = 1;
        }
    }

    /// <summary>
    /// Determines the relationship of startPerson to anchor.
    /// Returns the relationship type from the perspective of startPerson.
    /// </summary>
    private static RelationType? DetermineRelationship(GedcomGeniSync.Models.PersonRecord startPerson, string anchorId)
    {
        // Check if start is a child of anchor (anchor is parent)
        if (startPerson.FatherId == anchorId || startPerson.MotherId == anchorId)
        {
            return RelationType.Child;
        }

        // Check if start is a parent of anchor (anchor is child)
        if (startPerson.ChildrenIds.Contains(anchorId))
        {
            return RelationType.Parent;
        }

        // Check if start is a spouse of anchor
        if (startPerson.SpouseIds.Contains(anchorId))
        {
            return RelationType.Spouse;
        }

        // Check if start is a sibling of anchor
        if (startPerson.SiblingIds.Contains(anchorId))
        {
            return RelationType.Sibling;
        }

        return null;
    }

    /// <summary>
    /// Normalize GEDCOM ID by removing escape prefix if present.
    /// Command line preprocessing adds \ prefix to prevent @ from being interpreted.
    /// </summary>
    private static string NormalizeGedcomId(string id)
    {
        if (id.StartsWith("\\"))
        {
            return id.Substring(1);
        }
        return id;
    }
}

/// <summary>
/// Type of relationship between two persons
/// </summary>
public enum RelationType
{
    /// <summary>
    /// Person is a child of the related person
    /// </summary>
    Child,

    /// <summary>
    /// Person is a parent of the related person
    /// </summary>
    Parent,

    /// <summary>
    /// Person is a spouse/partner of the related person
    /// </summary>
    Spouse,

    /// <summary>
    /// Person is a sibling of the related person
    /// </summary>
    Sibling
}
