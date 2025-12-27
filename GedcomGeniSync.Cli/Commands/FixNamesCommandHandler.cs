using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Services.NameFix;
using GedcomGeniSync.Services.NameFix.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

/// <summary>
/// Command handler for fixing names across Geni profiles.
/// Performs BFS traversal from anchor profile and applies name fixes.
/// </summary>
public class FixNamesCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _anchorOption = new(
        "--anchor",
        "Geni profile ID to start BFS traversal from")
    { IsRequired = true };

    private readonly Option<string> _tokenFileOption = new(
        "--token-file",
        () => "geni_token.json",
        "Geni API token file path");

    private readonly Option<bool> _dryRunOption = new(
        "--dry-run",
        () => true,
        "Simulate changes without applying them");

    private readonly Option<int?> _maxDepthOption = new(
        "--max-depth",
        "Maximum BFS depth (unlimited if not specified)");

    private readonly Option<string> _progressFileOption = new(
        "--progress-file",
        () => "fix-names-progress.json",
        "File to save/resume progress");

    private readonly Option<string> _logFileOption = new(
        "--log-file",
        () => "fix-names.log",
        "File to log all changes");

    private readonly Option<bool> _resumeOption = new(
        "--resume",
        () => false,
        "Resume from previous progress file");

    private readonly Option<bool> _verboseOption = new(
        "--verbose",
        () => false,
        "Enable verbose logging");

    private readonly Option<string?> _handlersOption = new(
        "--handlers",
        "Comma-separated list of handlers to enable (default: all)");

    public FixNamesCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var command = new Command("fix-names", "Fix multilingual names across Geni profiles using BFS traversal");

        command.AddOption(_anchorOption);
        command.AddOption(_tokenFileOption);
        command.AddOption(_dryRunOption);
        command.AddOption(_maxDepthOption);
        command.AddOption(_progressFileOption);
        command.AddOption(_logFileOption);
        command.AddOption(_resumeOption);
        command.AddOption(_verboseOption);
        command.AddOption(_handlersOption);

        command.SetHandler(HandleAsync);
        return command;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var anchor = parseResult.GetValueForOption(_anchorOption)!;
        var tokenFile = parseResult.GetValueForOption(_tokenFileOption)!;
        var dryRun = parseResult.GetValueForOption(_dryRunOption);
        var maxDepth = parseResult.GetValueForOption(_maxDepthOption);
        var progressFile = parseResult.GetValueForOption(_progressFileOption)!;
        var logFile = parseResult.GetValueForOption(_logFileOption)!;
        var resume = parseResult.GetValueForOption(_resumeOption);
        var verbose = parseResult.GetValueForOption(_verboseOption);
        var handlersStr = parseResult.GetValueForOption(_handlersOption);

        // Parse enabled handlers
        var enabledHandlers = ParseHandlers(handlersStr);

        var accessToken = new Lazy<string>(() => AccessTokenResolver.ResolveFromFile(tokenFile));

        await using var scope = _startup.CreateScope(verbose, services =>
        {
            // Register profile client
            services.AddSingleton<IGeniProfileClient>(sp =>
            {
                return new GedcomGeniSync.ApiClient.Services.GeniProfileClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    accessToken.Value,
                    dryRun,
                    sp.GetRequiredService<ILogger<GedcomGeniSync.ApiClient.Services.GeniProfileClient>>());
            });

            // Register name fix handlers
            RegisterHandlers(services, enabledHandlers);

            // Register pipeline
            services.AddSingleton<INameFixPipeline, NameFixPipeline>();
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("FixNames");

        try
        {
            logger.LogInformation("=== Fix Names Command ===");
            logger.LogInformation("Anchor Profile: {Anchor}", anchor);
            logger.LogInformation("Dry Run: {DryRun}", dryRun);
            logger.LogInformation("Max Depth: {MaxDepth}", maxDepth?.ToString() ?? "unlimited");
            logger.LogInformation("Progress File: {ProgressFile}", progressFile);
            logger.LogInformation("Log File: {LogFile}", logFile);
            logger.LogInformation("Resume: {Resume}", resume);

            // List enabled handlers
            var pipeline = provider.GetRequiredService<INameFixPipeline>();
            logger.LogInformation("Enabled Handlers ({Count}):", pipeline.Handlers.Count);
            foreach (var handler in pipeline.Handlers)
            {
                logger.LogInformation("  - {Name} (order: {Order})", handler.Name, handler.Order);
            }

            // Create executor
            var profileClient = provider.GetRequiredService<IGeniProfileClient>();
            var executor = new FixNamesExecutor(
                profileClient,
                pipeline,
                logger,
                dryRun,
                maxDepth,
                progressFile,
                logFile);

            // Set up cancellation
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                logger.LogWarning("Cancellation requested. Finishing current profile and saving progress...");
                cts.Cancel();
            };

            // Execute
            var result = await executor.ExecuteAsync(anchor, resume, cts.Token);

            // Display results
            logger.LogInformation("");
            logger.LogInformation("=== Results ===");
            logger.LogInformation("Profiles Visited: {Count}", result.ProfilesVisited);
            logger.LogInformation("Profiles Changed: {Count}", result.ProfilesChanged);
            logger.LogInformation("Profiles Failed: {Count}", result.ProfilesFailed);
            logger.LogInformation("Total Changes: {Count}", result.TotalChanges);

            if (result.WasInterrupted)
            {
                logger.LogWarning("Processing was interrupted. Use --resume to continue.");
            }

            context.ExitCode = result.ProfilesFailed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fix-names command failed");
            context.ExitCode = 1;
        }
    }

    private HashSet<string>? ParseHandlers(string? handlersStr)
    {
        if (string.IsNullOrWhiteSpace(handlersStr)) return null;

        return handlersStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RegisterHandlers(IServiceCollection services, HashSet<string>? enabledHandlers)
    {
        // Register all handlers as INameFixHandler (ordered by execution priority)
        var handlerTypes = new (Type Type, string Name)[]
        {
            // Cleanup & extraction phase (Order: 5-15)
            (typeof(SpecialCharsCleanupHandler), "SpecialCharsCleanup"),   // Order: 5
            (typeof(TitleExtractHandler), "TitleExtract"),                 // Order: 8
            (typeof(ScriptSplitHandler), "ScriptSplit"),                   // Order: 10
            (typeof(SuffixExtractHandler), "SuffixExtract"),               // Order: 11
            (typeof(MaidenNameExtractHandler), "MaidenNameExtract"),       // Order: 12
            (typeof(NicknameExtractHandler), "NicknameExtract"),           // Order: 13
            (typeof(MarriedSurnameHandler), "MarriedSurname"),             // Order: 14
            (typeof(PatronymicHandler), "Patronymic"),                     // Order: 15

            // Script/Language detection phase (Order: 20-28)
            (typeof(CyrillicToRuHandler), "CyrillicToRu"),                 // Order: 20
            (typeof(UkrainianHandler), "Ukrainian"),                       // Order: 24
            (typeof(LithuanianHandler), "Lithuanian"),                     // Order: 25
            (typeof(EstonianHandler), "Estonian"),                         // Order: 26
            (typeof(LatinLanguageHandler), "LatinLanguage"),               // Order: 27
            (typeof(HebrewHandler), "Hebrew"),                             // Order: 28

            // Transliteration & normalization phase (Order: 30-42)
            (typeof(TranslitHandler), "Translit"),                         // Order: 30
            (typeof(EnsureEnglishHandler), "EnsureEnglish"),               // Order: 35 - MUST have English
            (typeof(FeminineSurnameHandler), "FeminineSurname"),           // Order: 40
            (typeof(SurnameParticleHandler), "SurnameParticle"),           // Order: 42

            // Final cleanup phase (Order: 95-100)
            (typeof(CapitalizationHandler), "Capitalization"),             // Order: 95
            (typeof(TypoDetectionHandler), "TypoDetection"),               // Order: 96 - Uses CSV dictionaries
            (typeof(DuplicateRemovalHandler), "DuplicateRemoval"),         // Order: 98
            (typeof(CleanupHandler), "Cleanup")                            // Order: 100
        };

        foreach (var (type, name) in handlerTypes)
        {
            // Skip if not in enabled list (when specified)
            if (enabledHandlers != null && !enabledHandlers.Contains(name))
            {
                continue;
            }

            // TypoDetectionHandler needs special registration with INameVariantsService
            if (type == typeof(TypoDetectionHandler))
            {
                services.AddSingleton<INameFixHandler>(sp =>
                {
                    var variantsService = sp.GetService<INameVariantsService>();
                    return variantsService != null
                        ? new TypoDetectionHandler(variantsService)
                        : new TypoDetectionHandler();
                });
            }
            else
            {
                services.AddSingleton(typeof(INameFixHandler), type);
            }
        }
    }
}
