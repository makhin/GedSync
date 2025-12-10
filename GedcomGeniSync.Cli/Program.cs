using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace GedcomGeniSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GEDCOM to Geni synchronization tool");

        var syncCommand = BuildSyncCommand();
        var analyzeCommand = BuildAnalyzeCommand();
        var authCommand = BuildAuthCommand();
        var profileCommand = BuildProfileCommand();

        rootCommand.AddCommand(syncCommand);
        rootCommand.AddCommand(analyzeCommand);
        rootCommand.AddCommand(authCommand);
        rootCommand.AddCommand(profileCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static Command BuildAuthCommand()
    {
        var authCommand = new Command("auth", "Authenticate with Geni using Desktop OAuth and save access token");

        var appKeyOption = new Option<string?>("--app-key", description: "Geni app key (or set GENI_APP_KEY env var)");
        var tokenFileOption = new Option<string>("--token-file", () => "geni_token.json", description: "Path to save token");
        var verboseOption = new Option<bool?>("--verbose", description: "Enable verbose logging");

        authCommand.AddOption(appKeyOption);
        authCommand.AddOption(tokenFileOption);
        authCommand.AddOption(verboseOption);

        authCommand.SetHandler(async context =>
        {
            var appKey = context.ParseResult.GetValueForOption(appKeyOption);
            var tokenFile = context.ParseResult.GetValueForOption(tokenFileOption)!;
            var verbose = context.ParseResult.GetValueForOption(verboseOption) ?? false;

            context.ExitCode = await RunAuthAsync(appKey, tokenFile, verbose, context.GetCancellationToken());
        });

        return authCommand;
    }

    private static Command BuildSyncCommand()
    {
        var syncCommand = new Command("sync", "Synchronize GEDCOM file to Geni");

        var configOption = new Option<string>("--config", description: "Path to configuration file (JSON or YAML)");
        var gedcomOption = new Option<string>("--gedcom", description: "Path to GEDCOM file") { IsRequired = true };
        var anchorGedOption = new Option<string>("--anchor-ged", description: "GEDCOM ID of anchor person (e.g., @I123@)") { IsRequired = true };
        var anchorGeniOption = new Option<string>("--anchor-geni", description: "Geni profile ID of anchor person") { IsRequired = true };
        var tokenOption = new Option<string>("--token", description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");
        var tokenFileOption = new Option<string>("--token-file", () => "geni_token.json", description: "Path to saved token file from auth command");
        var dryRunOption = new Option<bool?>("--dry-run", description: "Preview changes without creating profiles");
        var thresholdOption = new Option<int?>("--threshold", description: "Match threshold (0-100)");
        var maxDepthOption = new Option<int?>("--max-depth", description: "Maximum BFS depth (null for unlimited)");
        var stateFileOption = new Option<string>("--state-file", description: "Path to state file for resume support");
        var reportFileOption = new Option<string>("--report", description: "Path to save report");
        var givenNamesOption = new Option<string>("--given-names-csv", description: "Path to given names variants CSV");
        var surnamesOption = new Option<string>("--surnames-csv", description: "Path to surnames variants CSV");
        var verboseOption = new Option<bool?>("--verbose", description: "Enable verbose logging");
        var syncPhotosOption = new Option<bool?>("--sync-photos", description: "Enable photo synchronization from MyHeritage");

        syncCommand.AddOption(configOption);
        syncCommand.AddOption(gedcomOption);
        syncCommand.AddOption(anchorGedOption);
        syncCommand.AddOption(anchorGeniOption);
        syncCommand.AddOption(tokenOption);
        syncCommand.AddOption(tokenFileOption);
        syncCommand.AddOption(dryRunOption);
        syncCommand.AddOption(thresholdOption);
        syncCommand.AddOption(maxDepthOption);
        syncCommand.AddOption(stateFileOption);
        syncCommand.AddOption(reportFileOption);
        syncCommand.AddOption(givenNamesOption);
        syncCommand.AddOption(surnamesOption);
        syncCommand.AddOption(verboseOption);
        syncCommand.AddOption(syncPhotosOption);

        syncCommand.SetHandler(async context =>
        {
            // Load configuration
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var configLoader = new ConfigurationLoader();
            var config = configLoader.LoadWithDefaults(
                configPath ?? "",
                "gedsync.json",
                "gedsync.yaml",
                "gedsync.yml",
                ".gedsync.json",
                ".gedsync.yaml",
                ".gedsync.yml"
            );

            // Get CLI options (they override config)
            var gedcomPath = context.ParseResult.GetValueForOption(gedcomOption)!;
            var anchorGed = context.ParseResult.GetValueForOption(anchorGedOption)!;
            var anchorGeni = context.ParseResult.GetValueForOption(anchorGeniOption)!;
            var token = context.ParseResult.GetValueForOption(tokenOption);
            var tokenFile = context.ParseResult.GetValueForOption(tokenFileOption)!;
            var dryRunCli = context.ParseResult.GetValueForOption(dryRunOption);
            var thresholdCli = context.ParseResult.GetValueForOption(thresholdOption);
            var maxDepthCli = context.ParseResult.GetValueForOption(maxDepthOption);
            var stateFileCli = context.ParseResult.GetValueForOption(stateFileOption);
            var reportFileCli = context.ParseResult.GetValueForOption(reportFileOption);
            var givenNamesCsvCli = context.ParseResult.GetValueForOption(givenNamesOption);
            var surnamesCsvCli = context.ParseResult.GetValueForOption(surnamesOption);
            var verboseCli = context.ParseResult.GetValueForOption(verboseOption);
            var syncPhotosCli = context.ParseResult.GetValueForOption(syncPhotosOption);

            // Merge CLI options with config (CLI takes precedence)
            var dryRun = dryRunCli ?? config.Sync.DryRun;
            var threshold = thresholdCli ?? config.Matching.MatchThreshold;
            var maxDepth = maxDepthCli ?? config.Sync.MaxDepth;
            var stateFile = stateFileCli ?? config.Paths.StateFile;
            var reportFile = reportFileCli ?? config.Paths.ReportFile;
            var givenNamesCsv = givenNamesCsvCli ?? config.NameVariants.GivenNamesCsv;
            var surnamesCsv = surnamesCsvCli ?? config.NameVariants.SurnamesCsv;
            var verbose = verboseCli ?? config.Logging.Verbose;
            var syncPhotos = syncPhotosCli ?? config.Sync.SyncPhotos;

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
            await using var provider = BuildServiceProvider(verbose, services =>
            {
                // Use configuration with CLI override for threshold
                var matchingOptions = config.Matching
                    .ToMatchingOptions() with { MatchThreshold = threshold };
                services.AddSingleton(matchingOptions);
                services.AddSingleton(sp => new SyncOptions
                {
                    StateFilePath = stateFile,
                    MaxDepth = maxDepth,
                    MatchingOptions = sp.GetRequiredService<MatchingOptions>(),
                    SyncPhotos = syncPhotos,
                    DryRun = dryRun
                });
                services.AddSingleton<INameVariantsService, NameVariantsService>();
                services.AddSingleton<IFuzzyMatcherService>(sp => new FuzzyMatcherService(
                    sp.GetRequiredService<INameVariantsService>(),
                    sp.GetRequiredService<ILogger<FuzzyMatcherService>>(),
                    sp.GetRequiredService<MatchingOptions>()));
                services.AddSingleton<IGedcomLoader>(sp => new GedcomLoader(sp.GetRequiredService<ILogger<GedcomLoader>>()));
                services.AddSingleton<IGeniApiClient>(sp => new GeniApiClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    token ?? string.Empty,
                    dryRun,
                    sp.GetRequiredService<ILogger<GeniApiClient>>()));
                services.AddSingleton<IMyHeritagePhotoService>(sp => new MyHeritagePhotoService(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<MyHeritagePhotoService>>(),
                    dryRun));
                services.AddSingleton<ISyncStateManager, SyncStateManager>();
                services.AddSingleton<ISyncService>(sp => new SyncService(
                    sp.GetRequiredService<IGedcomLoader>(),
                    sp.GetRequiredService<IGeniApiClient>(),
                    sp.GetRequiredService<IFuzzyMatcherService>(),
                    sp.GetRequiredService<ISyncStateManager>(),
                    sp.GetRequiredService<ILogger<SyncService>>(),
                    sp.GetRequiredService<SyncOptions>(),
                    syncPhotos ? sp.GetRequiredService<IMyHeritagePhotoService>() : null));
            });

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
                if (!string.IsNullOrEmpty(givenNamesCsv) || !string.IsNullOrEmpty(surnamesCsv))
                {
                    nameVariants.LoadFromCsv(givenNamesCsv ?? string.Empty, surnamesCsv ?? string.Empty);
                }

                logger.LogInformation("=== GEDCOM to Geni Sync ===");
                logger.LogInformation("Mode: {Mode}", dryRun ? "DRY-RUN (no changes)" : "LIVE");
                logger.LogInformation("Match threshold: {Threshold}%", threshold);
                logger.LogInformation("Photo sync: {Status}", syncPhotos ? "ENABLED" : "DISABLED");
                logger.LogInformation("Max depth: {Depth}", maxDepth?.ToString() ?? "unlimited");
                logger.LogInformation("State file: {StateFile} | Report file: {ReportFile}",
                    stateFile ?? "<not set>", reportFile ?? "<not set>");
                logger.LogInformation("Name variants: given={GivenNames}, surnames={Surnames}",
                    givenNamesCsv ?? "<none>", surnamesCsv ?? "<none>");

                var syncService = provider.GetRequiredService<ISyncService>();
                var report = await syncService.SyncAsync(
                    gedcomPath, anchorGed, anchorGeni, context.GetCancellationToken());

                report.PrintSummary(logger);

                if (verbose)
                {
                    report.PrintDetails(logger);
                }

                if (!string.IsNullOrEmpty(reportFile))
                {
                    await report.SaveToFileAsync(reportFile);
                    logger.LogInformation("Report saved to: {Path}", reportFile);
                }

                context.ExitCode = report.Errors > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync failed");
                context.ExitCode = 1;
            }
        });

        return syncCommand;
    }

    private static Command BuildAnalyzeCommand()
    {
        var analyzeCommand = new Command("analyze", "Analyze GEDCOM file without syncing");
        var analyzeGedcomOption = new Option<string>("--gedcom", description: "Path to GEDCOM file") { IsRequired = true };
        var analyzeAnchorOption = new Option<string?>("--anchor", description: "GEDCOM ID to start BFS from (optional)");

        analyzeCommand.AddOption(analyzeGedcomOption);
        analyzeCommand.AddOption(analyzeAnchorOption);

        analyzeCommand.SetHandler(async context =>
        {
            var gedcomPath = context.ParseResult.GetValueForOption(analyzeGedcomOption)!;
            var anchor = context.ParseResult.GetValueForOption(analyzeAnchorOption);

            await using var provider = BuildServiceProvider(verbose: true, services =>
            {
                services.AddSingleton(sp => new GedcomLoader(sp.GetRequiredService<ILogger<GedcomLoader>>()));
            });

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Analyze");

            try
            {
                logger.LogInformation("=== GEDCOM Analysis ===");

                var gedcomLoader = provider.GetRequiredService<GedcomLoader>();
                var result = gedcomLoader.Load(gedcomPath);

                result.PrintStats(logger);

                if (!string.IsNullOrEmpty(anchor))
                {
                    // Normalize anchor ID to standard GEDCOM format
                    var resolvedAnchor = GedcomIdNormalizer.Normalize(anchor);

                    logger.LogInformation("\n=== BFS from {Anchor} ===", anchor);

                    var count = 0;
                    foreach (var person in result.TraverseBfs(resolvedAnchor, maxDepth: 3))
                    {
                        var relations = new List<string>();
                        if (!string.IsNullOrEmpty(person.FatherId)) relations.Add($"father:{person.FatherId}");
                        if (!string.IsNullOrEmpty(person.MotherId)) relations.Add($"mother:{person.MotherId}");
                        if (person.SpouseIds.Any()) relations.Add($"spouses:{person.SpouseIds.Count}");
                        if (person.ChildrenIds.Any()) relations.Add($"children:{person.ChildrenIds.Count}");

                        logger.LogInformation("  {Person} [{Relations}]", person, string.Join(", ", relations));

                        count++;
                        if (count >= 50)
                        {
                            logger.LogInformation("  ... (showing first 50)");
                            break;
                        }
                    }
                }

                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Analysis failed");
                context.ExitCode = 1;
            }

            await Task.CompletedTask;
        });

        return analyzeCommand;
    }

    private static Command BuildProfileCommand()
    {
        var profileCommand = new Command("profile", "Get current user's Geni profile information");

        var tokenOption = new Option<string?>("--token", description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");
        var tokenFileOption = new Option<string>("--token-file", () => "geni_token.json", description: "Path to saved token file from auth command");
        var verboseOption = new Option<bool?>("--verbose", description: "Enable verbose logging");

        profileCommand.AddOption(tokenOption);
        profileCommand.AddOption(tokenFileOption);
        profileCommand.AddOption(verboseOption);

        profileCommand.SetHandler(async context =>
        {
            var token = context.ParseResult.GetValueForOption(tokenOption);
            var tokenFile = context.ParseResult.GetValueForOption(tokenFileOption)!;
            var verbose = context.ParseResult.GetValueForOption(verboseOption) ?? false;

            await using var provider = BuildServiceProvider(verbose, services =>
            {
                services.AddSingleton<IGeniApiClient>(sp =>
                {
                    var resolvedToken = token ?? Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");

                    if (string.IsNullOrWhiteSpace(resolvedToken))
                    {
                        var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile).Result;
                        if (storedToken != null && !storedToken.IsExpired)
                        {
                            resolvedToken = storedToken.AccessToken;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(resolvedToken))
                    {
                        throw new InvalidOperationException("No valid token found. Run 'auth' command first.");
                    }

                    return new GeniApiClient(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        resolvedToken,
                        dryRun: false,
                        sp.GetRequiredService<ILogger<GeniApiClient>>());
                });
            });

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Profile");

            try
            {
                var geniClient = provider.GetRequiredService<IGeniApiClient>();
                var profile = await geniClient.GetCurrentUserProfileAsync();

                if (profile == null)
                {
                    logger.LogError("Failed to get profile");
                    context.ExitCode = 1;
                    return;
                }

                logger.LogInformation("");
                logger.LogInformation("=== Your Geni Profile ===");
                logger.LogInformation("Name: {Name}", profile.FirstName + " " + profile.LastName);
                logger.LogInformation("Numeric ID: {Id}", profile.Id.Replace("profile-", ""));
                logger.LogInformation("GUID: {Guid}", profile.Guid);
                logger.LogInformation("");
                logger.LogInformation("For sync command use:");
                logger.LogInformation("  --anchor-geni {Id}", profile.Id.Replace("profile-", ""));

                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get profile");
                context.ExitCode = 1;
            }
        });

        return profileCommand;
    }

    private static async Task<int> RunAuthAsync(
        string? appKey,
        string tokenFile,
        bool verbose,
        CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("Auth");

        appKey ??= Environment.GetEnvironmentVariable("GENI_APP_KEY");

        if (string.IsNullOrEmpty(appKey))
        {
            logger.LogError("App key required. Use --app-key or set GENI_APP_KEY environment variable");
            return 1;
        }

        logger.LogInformation("=== Geni Authentication (Desktop OAuth) ===");

        IGeniAuthClient authClient = new GeniAuthClient(appKey, logger);

        var existingToken = await authClient.LoadTokenAsync(tokenFile);
        if (existingToken != null && !existingToken.IsExpired)
        {
            logger.LogInformation("Valid token already exists at {Path}", tokenFile);
            logger.LogInformation("Access token: {Token}...", existingToken.AccessToken[..Math.Min(20, existingToken.AccessToken.Length)]);
            logger.LogInformation("Expires at: {ExpiresAt}", existingToken.ExpiresAt);
            return 0;
        }

        var token = await authClient.LoginInteractiveAsync(cancellationToken);

        if (token == null)
        {
            logger.LogError("Authentication failed");
            return 1;
        }

        await authClient.SaveTokenAsync(token, tokenFile);

        logger.LogInformation("Access token: {Token}...", token.AccessToken[..Math.Min(20, token.AccessToken.Length)]);
        logger.LogInformation("Saved token to {Path}", tokenFile);
        logger.LogInformation("Use this token with --token option or set GENI_ACCESS_TOKEN env var");

        return 0;
    }

    private static ServiceProvider BuildServiceProvider(bool verbose, Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Configure HttpClient factory for GeniApiClient
        services.AddHttpClient("GeniApi", client =>
        {
            client.BaseAddress = new Uri("https://www.geni.com/api");
        });

        // Configure HttpClient factory for MyHeritagePhotoService
        services.AddHttpClient("MyHeritagePhoto", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for photo downloads
        });

        configureServices(services);
        return services.BuildServiceProvider();
    }
}
