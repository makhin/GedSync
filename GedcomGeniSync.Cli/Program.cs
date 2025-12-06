using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
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
        var testMatchCommand = new Command("test-match", "Run fuzzy matching tests");

        testMatchCommand.SetHandler(async context =>
        {
            await using var provider = BuildServiceProvider(verbose: true, services =>
            {
                services.AddSingleton<NameVariantsService>();
                services.AddSingleton(sp => new FuzzyMatcherService(
                    sp.GetRequiredService<NameVariantsService>(),
                    sp.GetRequiredService<ILogger<FuzzyMatcherService>>()));
            });

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("TestMatch");
            await RunTestMatchAsync(logger, provider.GetRequiredService<NameVariantsService>(),
                provider.GetRequiredService<FuzzyMatcherService>());
            context.ExitCode = 0;
        });

        rootCommand.AddCommand(syncCommand);
        rootCommand.AddCommand(analyzeCommand);
        rootCommand.AddCommand(testMatchCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static Command BuildSyncCommand()
    {
        var syncCommand = new Command("sync", "Synchronize GEDCOM file to Geni");

        var configOption = new Option<string>("--config", description: "Path to configuration file (JSON or YAML)");
        var gedcomOption = new Option<string>("--gedcom", description: "Path to GEDCOM file") { IsRequired = true };
        var anchorGedOption = new Option<string>("--anchor-ged", description: "GEDCOM ID of anchor person (e.g., @I123@)") { IsRequired = true };
        var anchorGeniOption = new Option<string>("--anchor-geni", description: "Geni profile ID of anchor person") { IsRequired = true };
        var tokenOption = new Option<string>("--token", description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");
        var dryRunOption = new Option<bool?>("--dry-run", description: "Preview changes without creating profiles");
        var thresholdOption = new Option<int?>("--threshold", description: "Match threshold (0-100)");
        var maxDepthOption = new Option<int?>("--max-depth", description: "Maximum BFS depth (null for unlimited)");
        var stateFileOption = new Option<string>("--state-file", description: "Path to state file for resume support");
        var reportFileOption = new Option<string>("--report", description: "Path to save report");
        var givenNamesOption = new Option<string>("--given-names-csv", description: "Path to given names variants CSV");
        var surnamesOption = new Option<string>("--surnames-csv", description: "Path to surnames variants CSV");
        var verboseOption = new Option<bool?>("--verbose", description: "Enable verbose logging");

        syncCommand.AddOption(configOption);
        syncCommand.AddOption(gedcomOption);
        syncCommand.AddOption(anchorGedOption);
        syncCommand.AddOption(anchorGeniOption);
        syncCommand.AddOption(tokenOption);
        syncCommand.AddOption(dryRunOption);
        syncCommand.AddOption(thresholdOption);
        syncCommand.AddOption(maxDepthOption);
        syncCommand.AddOption(stateFileOption);
        syncCommand.AddOption(reportFileOption);
        syncCommand.AddOption(givenNamesOption);
        syncCommand.AddOption(surnamesOption);
        syncCommand.AddOption(verboseOption);

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
            var dryRunCli = context.ParseResult.GetValueForOption(dryRunOption);
            var thresholdCli = context.ParseResult.GetValueForOption(thresholdOption);
            var maxDepthCli = context.ParseResult.GetValueForOption(maxDepthOption);
            var stateFileCli = context.ParseResult.GetValueForOption(stateFileOption);
            var reportFileCli = context.ParseResult.GetValueForOption(reportFileOption);
            var givenNamesCsvCli = context.ParseResult.GetValueForOption(givenNamesOption);
            var surnamesCsvCli = context.ParseResult.GetValueForOption(surnamesOption);
            var verboseCli = context.ParseResult.GetValueForOption(verboseOption);

            // Merge CLI options with config (CLI takes precedence)
            var dryRun = dryRunCli ?? config.Sync.DryRun;
            var threshold = thresholdCli ?? config.Matching.MatchThreshold;
            var maxDepth = maxDepthCli ?? config.Sync.MaxDepth;
            var stateFile = stateFileCli ?? config.Paths.StateFile;
            var reportFile = reportFileCli ?? config.Paths.ReportFile;
            var givenNamesCsv = givenNamesCsvCli ?? config.NameVariants.GivenNamesCsv;
            var surnamesCsv = surnamesCsvCli ?? config.NameVariants.SurnamesCsv;
            var verbose = verboseCli ?? config.Logging.Verbose;

            token ??= Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");
            await using var provider = BuildServiceProvider(verbose, services =>
            {
                // Use configuration with CLI override for threshold
                var matchingOptions = config.Matching.ToMatchingOptions();
                matchingOptions.MatchThreshold = threshold;
                services.AddSingleton(matchingOptions);
                services.AddSingleton(sp => new SyncOptions
                {
                    StateFilePath = stateFile,
                    MaxDepth = maxDepth,
                    MatchingOptions = sp.GetRequiredService<MatchingOptions>()
                });
                services.AddSingleton<NameVariantsService>();
                services.AddSingleton(sp => new FuzzyMatcherService(
                    sp.GetRequiredService<NameVariantsService>(),
                    sp.GetRequiredService<ILogger<FuzzyMatcherService>>(),
                    sp.GetRequiredService<MatchingOptions>()));
                services.AddSingleton(sp => new GedcomLoader(sp.GetRequiredService<ILogger<GedcomLoader>>()));
                services.AddSingleton(sp => new GeniApiClient(token ?? string.Empty, dryRun, sp.GetRequiredService<ILogger<GeniApiClient>>()));
                services.AddSingleton(sp => new SyncService(
                    sp.GetRequiredService<GedcomLoader>(),
                    sp.GetRequiredService<GeniApiClient>(),
                    sp.GetRequiredService<FuzzyMatcherService>(),
                    sp.GetRequiredService<ILogger<SyncService>>(),
                    sp.GetRequiredService<SyncOptions>()));
            });

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sync");

            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    logger.LogError("Geni access token required. Use --token or set GENI_ACCESS_TOKEN");
                    context.ExitCode = 1;
                    return;
                }

                var nameVariants = provider.GetRequiredService<NameVariantsService>();
                if (!string.IsNullOrEmpty(givenNamesCsv) || !string.IsNullOrEmpty(surnamesCsv))
                {
                    nameVariants.LoadFromCsv(givenNamesCsv ?? string.Empty, surnamesCsv ?? string.Empty);
                }

                logger.LogInformation("=== GEDCOM to Geni Sync ===");
                logger.LogInformation("Mode: {Mode}", dryRun ? "DRY-RUN (no changes)" : "LIVE");
                logger.LogInformation("Match threshold: {Threshold}%", threshold);

                var syncService = provider.GetRequiredService<SyncService>();
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

                logger.LogInformation("\n=== Sample Persons ===");
                foreach (var person in result.Persons.Values.Take(10))
                {
                    logger.LogInformation("{Id}: {Name}", person.Id, person);
                }

                if (!string.IsNullOrEmpty(anchor))
                {
                    logger.LogInformation("\n=== BFS from {Anchor} ===", anchor);

                    var count = 0;
                    foreach (var person in result.TraverseBfs(anchor, maxDepth: 3))
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

    // Helper method to create PersonRecord with normalized names
    private static PersonRecord CreateTestPerson(string id, PersonSource source, string? firstName, string? lastName, string? maidenName = null, string? birthDate = null)
    {
        return new PersonRecord
        {
            Id = id,
            Source = source,
            FirstName = firstName,
            LastName = lastName,
            MaidenName = maidenName,
            NormalizedFirstName = NameNormalizer.Normalize(firstName),
            NormalizedLastName = NameNormalizer.Normalize(lastName),
            BirthDate = DateInfo.Parse(birthDate)
        };
    }

    private static async Task RunTestMatchAsync(
        ILogger logger,
        NameVariantsService nameVariants,
        FuzzyMatcherService matcher)
    {
        logger.LogInformation("=== Testing Fuzzy Matching ===\n");

        var testCases = new[]
        {
            (CreateTestPerson("TEST1", PersonSource.Gedcom, "Иван", "Петров", null, "1885"),
             CreateTestPerson("TEST1", PersonSource.Geni, "Иван", "Петров", null, "1885"),
             "Exact match"),
            (CreateTestPerson("TEST2", PersonSource.Gedcom, "Иван", "Петров", null, "1885"),
             CreateTestPerson("TEST2", PersonSource.Geni, "Ivan", "Petrov", null, "1885"),
             "Cyrillic vs Latin"),
            (CreateTestPerson("TEST3", PersonSource.Gedcom, "Иван", "Петров", null, "1885"),
             CreateTestPerson("TEST3", PersonSource.Geni, "John", "Petrov", null, "1885"),
             "Ivan = John equivalent"),
            (CreateTestPerson("TEST4", PersonSource.Gedcom, "Мария", "Сидорова", null, "1890"),
             CreateTestPerson("TEST4", PersonSource.Geni, "Maria", "Sidorova", null, "1892"),
             "Date ±2 years"),
            (CreateTestPerson("TEST5", PersonSource.Gedcom, "Анна", "Петрова", "Иванова", "1888"),
             CreateTestPerson("TEST5", PersonSource.Geni, "Anna", "Иванова", null, "1888"),
             "Maiden name match"),
            (CreateTestPerson("TEST6", PersonSource.Gedcom, "Иван", "Петров", null, "1885"),
             CreateTestPerson("TEST6", PersonSource.Geni, "Пётр", "Сидоров", null, "1920"),
             "Different persons"),
            (CreateTestPerson("TEST7", PersonSource.Gedcom, "Александр", "Смирнов", null, "1900"),
             CreateTestPerson("TEST7", PersonSource.Geni, "Саша", "Смирнов", null, "1900"),
             "Александр = Саша")
        };

        foreach (var (source, target, description) in testCases)
        {
            var result = matcher.Compare(source, target);

            logger.LogInformation("Test: {Description}", description);
            logger.LogInformation("  {Source} vs {Target}", source.FullName, target.FullName);
            logger.LogInformation("  Score: {Score}%", result.Score);

            foreach (var reason in result.Reasons)
            {
                logger.LogInformation("    {Field}: +{Points} ({Details})",
                    reason.Field, reason.Points, reason.Details);
            }

            logger.LogInformation(string.Empty);
        }

        await Task.CompletedTask;
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

        configureServices(services);
        return services.BuildServiceProvider();
    }
}
