using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using System.CommandLine;
using System.Text.Json;

namespace GedcomGeniSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GEDCOM to Geni synchronization tool");

        // Sync command
        var syncCommand = new Command("sync", "Synchronize GEDCOM file to Geni");
        
        var gedcomOption = new Option<string>(
            name: "--gedcom",
            description: "Path to GEDCOM file")
        { IsRequired = true };

        var anchorGedOption = new Option<string>(
            name: "--anchor-ged",
            description: "GEDCOM ID of anchor person (e.g., @I123@)")
        { IsRequired = true };

        var anchorGeniOption = new Option<string>(
            name: "--anchor-geni",
            description: "Geni profile ID of anchor person")
        { IsRequired = true };

        var tokenOption = new Option<string>(
            name: "--token",
            description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Preview changes without creating profiles",
            getDefaultValue: () => true);

        var thresholdOption = new Option<int>(
            name: "--threshold",
            description: "Match threshold (0-100)",
            getDefaultValue: () => 70);

        var maxDepthOption = new Option<int?>(
            name: "--max-depth",
            description: "Maximum BFS depth (null for unlimited)");

        var stateFileOption = new Option<string>(
            name: "--state-file",
            description: "Path to state file for resume support",
            getDefaultValue: () => "sync_state.json");

        var reportFileOption = new Option<string>(
            name: "--report",
            description: "Path to save report",
            getDefaultValue: () => "sync_report.json");

        var givenNamesOption = new Option<string>(
            name: "--given-names-csv",
            description: "Path to given names variants CSV");

        var surnamesOption = new Option<string>(
            name: "--surnames-csv",
            description: "Path to surnames variants CSV");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging",
            getDefaultValue: () => false);

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

        syncCommand.SetHandler(async (context) =>
        {
            var gedcomPath = context.ParseResult.GetValueForOption(gedcomOption)!;
            var anchorGed = context.ParseResult.GetValueForOption(anchorGedOption)!;
            var anchorGeni = context.ParseResult.GetValueForOption(anchorGeniOption)!;
            var token = context.ParseResult.GetValueForOption(tokenOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var threshold = context.ParseResult.GetValueForOption(thresholdOption);
            var maxDepth = context.ParseResult.GetValueForOption(maxDepthOption);
            var stateFile = context.ParseResult.GetValueForOption(stateFileOption);
            var reportFile = context.ParseResult.GetValueForOption(reportFileOption);
            var givenNamesCsv = context.ParseResult.GetValueForOption(givenNamesOption);
            var surnamesCsv = context.ParseResult.GetValueForOption(surnamesOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            var exitCode = await RunSyncAsync(
                gedcomPath, anchorGed, anchorGeni, token,
                dryRun, threshold, maxDepth, stateFile, reportFile,
                givenNamesCsv, surnamesCsv, verbose,
                context.GetCancellationToken());

            context.ExitCode = exitCode;
        });

        // Analyze command (just load and show stats)
        var analyzeCommand = new Command("analyze", "Analyze GEDCOM file without syncing");
        
        var analyzeGedcomOption = new Option<string>(
            name: "--gedcom",
            description: "Path to GEDCOM file")
        { IsRequired = true };

        var analyzeAnchorOption = new Option<string?>(
            name: "--anchor",
            description: "GEDCOM ID to start BFS from (optional)");

        analyzeCommand.AddOption(analyzeGedcomOption);
        analyzeCommand.AddOption(analyzeAnchorOption);

        analyzeCommand.SetHandler(async (gedcomPath, anchor) =>
        {
            await RunAnalyzeAsync(gedcomPath, anchor);
        }, analyzeGedcomOption, analyzeAnchorOption);

        // Test-match command (test matching between two persons)
        var testMatchCommand = new Command("test-match", "Test matching logic with sample data");
        testMatchCommand.SetHandler(RunTestMatchAsync);

        rootCommand.AddCommand(syncCommand);
        rootCommand.AddCommand(analyzeCommand);
        rootCommand.AddCommand(testMatchCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task<int> RunSyncAsync(
        string gedcomPath,
        string anchorGed,
        string anchorGeni,
        string? token,
        bool dryRun,
        int threshold,
        int? maxDepth,
        string? stateFile,
        string? reportFile,
        string? givenNamesCsv,
        string? surnamesCsv,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var logger = new ConsoleLogger { Verbose = verbose };

        try
        {
            // Get token from env if not provided
            token ??= Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                logger.LogError(null!, "Geni access token required. Use --token or set GENI_ACCESS_TOKEN", Array.Empty<object>());
                return 1;
            }

            logger.LogInformation("=== GEDCOM to Geni Sync ===");
            logger.LogInformation("Mode: {Mode}", dryRun ? "DRY-RUN (no changes)" : "LIVE");
            logger.LogInformation("Match threshold: {Threshold}%", threshold);

            // Initialize services
            var nameVariants = new NameVariantsService(logger);
            
            if (!string.IsNullOrEmpty(givenNamesCsv) || !string.IsNullOrEmpty(surnamesCsv))
            {
                nameVariants.LoadFromCsv(
                    givenNamesCsv ?? string.Empty,
                    surnamesCsv ?? string.Empty);
            }

            var matchingOptions = new MatchingOptions
            {
                MatchThreshold = threshold
            };

            var matcher = new FuzzyMatcherService(nameVariants, logger, matchingOptions);
            var gedcomLoader = new GedcomLoader(logger);
            var geniClient = new GeniApiClient(token, dryRun, logger);

            var syncOptions = new SyncOptions
            {
                StateFilePath = stateFile,
                MaxDepth = maxDepth,
                MatchingOptions = matchingOptions
            };

            var syncService = new SyncService(
                gedcomLoader, geniClient, matcher, logger, syncOptions);

            // Run sync
            var report = await syncService.SyncAsync(
                gedcomPath, anchorGed, anchorGeni, cancellationToken);

            // Output results
            report.PrintSummary(logger);

            if (verbose)
            {
                report.PrintDetails(logger);
            }

            // Save report
            if (!string.IsNullOrEmpty(reportFile))
            {
                await report.SaveToFileAsync(reportFile);
                logger.LogInformation("Report saved to: {Path}", reportFile);
            }

            return report.Errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed", Array.Empty<object>());
            return 1;
        }
    }

    static async Task RunAnalyzeAsync(string gedcomPath, string? anchor)
    {
        var logger = new ConsoleLogger { Verbose = true };

        try
        {
            logger.LogInformation("=== GEDCOM Analysis ===");
            
            var gedcomLoader = new GedcomLoader(logger);
            var result = gedcomLoader.Load(gedcomPath);
            
            result.PrintStats(logger);

            // Show sample persons
            logger.LogInformation("\n=== Sample Persons ===");
            var samples = result.Persons.Values.Take(10);
            foreach (var person in samples)
            {
                logger.LogInformation("{Id}: {Name}", person.Id, person);
            }

            // BFS from anchor if provided
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

                    logger.LogInformation("  {Person} [{Relations}]", 
                        person, string.Join(", ", relations));
                    
                    count++;
                    if (count >= 50) 
                    {
                        logger.LogInformation("  ... (showing first 50)");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis failed", Array.Empty<object>());
        }

        await Task.CompletedTask;
    }

    static async Task RunTestMatchAsync()
    {
        var logger = new ConsoleLogger { Verbose = true };
        var nameVariants = new NameVariantsService(logger);
        var matcher = new FuzzyMatcherService(nameVariants, logger);

        logger.LogInformation("=== Testing Fuzzy Matching ===\n");

        // Test cases
        var testCases = new[]
        {
            // Exact match
            (
                new PersonRecord { FirstName = "Иван", LastName = "Петров", BirthDate = DateInfo.Parse("1885") },
                new PersonRecord { FirstName = "Иван", LastName = "Петров", BirthDate = DateInfo.Parse("1885") },
                "Exact match"
            ),
            // Transliteration
            (
                new PersonRecord { FirstName = "Иван", LastName = "Петров", BirthDate = DateInfo.Parse("1885") },
                new PersonRecord { FirstName = "Ivan", LastName = "Petrov", BirthDate = DateInfo.Parse("1885") },
                "Cyrillic vs Latin"
            ),
            // Name variants
            (
                new PersonRecord { FirstName = "Иван", LastName = "Петров", BirthDate = DateInfo.Parse("1885") },
                new PersonRecord { FirstName = "John", LastName = "Petrov", BirthDate = DateInfo.Parse("1885") },
                "Ivan = John equivalent"
            ),
            // Fuzzy date
            (
                new PersonRecord { FirstName = "Мария", LastName = "Сидорова", BirthDate = DateInfo.Parse("1890") },
                new PersonRecord { FirstName = "Maria", LastName = "Sidorova", BirthDate = DateInfo.Parse("1892") },
                "Date ±2 years"
            ),
            // Maiden name
            (
                new PersonRecord { FirstName = "Анна", LastName = "Петрова", MaidenName = "Иванова", BirthDate = DateInfo.Parse("1888") },
                new PersonRecord { FirstName = "Anna", LastName = "Иванова", BirthDate = DateInfo.Parse("1888") },
                "Maiden name match"
            ),
            // Different persons
            (
                new PersonRecord { FirstName = "Иван", LastName = "Петров", BirthDate = DateInfo.Parse("1885") },
                new PersonRecord { FirstName = "Пётр", LastName = "Сидоров", BirthDate = DateInfo.Parse("1920") },
                "Different persons"
            ),
            // Nickname
            (
                new PersonRecord { FirstName = "Александр", LastName = "Смирнов", BirthDate = DateInfo.Parse("1900") },
                new PersonRecord { FirstName = "Саша", LastName = "Смирнов", BirthDate = DateInfo.Parse("1900") },
                "Александр = Саша"
            )
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
            
            logger.LogInformation("");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Enhanced console logger with verbose mode
/// </summary>
public class ConsoleLogger : ILogger
{
    public bool Verbose { get; set; }

    public void LogDebug(string message, params object[] args)
    {
        if (Verbose)
        {
            WriteColored(ConsoleColor.Gray, "[DEBUG] ", message, args);
        }
    }

    public void LogInformation(string message, params object[] args)
    {
        WriteColored(ConsoleColor.White, "[INFO] ", message, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        WriteColored(ConsoleColor.Yellow, "[WARN] ", message, args);
    }

    public void LogError(Exception? ex, string message, params object[] args)
    {
        WriteColored(ConsoleColor.Red, "[ERROR] ", message, args);
        if (ex != null && Verbose)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private static void WriteColored(ConsoleColor color, string prefix, string message, object[] args)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        
        try
        {
            var formatted = FormatMessage(message, args);
            Console.WriteLine($"{prefix}{formatted}");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private static string FormatMessage(string message, object[] args)
    {
        if (args.Length == 0)
            return message;

        // Replace {Name} placeholders with {0}, {1}, etc.
        var index = 0;
        var result = System.Text.RegularExpressions.Regex.Replace(
            message, 
            @"\{[^}]+\}", 
            _ => $"{{{index++}}}");

        try
        {
            return string.Format(result, args);
        }
        catch
        {
            return message;
        }
    }
}
