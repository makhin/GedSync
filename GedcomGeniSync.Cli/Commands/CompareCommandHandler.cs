using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using GedcomGeniSync.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class CompareCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string?> _configOption = new("--config", description: "Path to configuration file (JSON or YAML)");
    private readonly Option<string> _sourceOption = new("--source", description: "Path to source GEDCOM file") { IsRequired = true };
    private readonly Option<string> _destOption = new("--dest", description: "Path to destination GEDCOM file") { IsRequired = true };
    private readonly Option<string> _anchorSourceOption = new("--anchor-source", description: "GEDCOM ID of anchor person in source (e.g., @I123@)") { IsRequired = true };
    private readonly Option<string> _anchorDestOption = new("--anchor-dest", description: "GEDCOM ID of anchor person in destination (e.g., @I456@)") { IsRequired = true };
    private readonly Option<string?> _outputOption = new("--output", description: "Output JSON file path (default: stdout)");
    private readonly Option<int?> _depthOption = new("--depth", description: "Depth of new nodes to add from existing matched nodes");
    private readonly Option<int?> _thresholdOption = new("--threshold", description: "Match threshold (0-100)");
    private readonly Option<bool?> _includeDeletesOption = new("--include-deletes", description: "Include delete suggestions");
    private readonly Option<bool?> _requireUniqueOption = new("--require-unique", description: "Require unique matches");
    private readonly Option<bool?> _verboseOption = new("--verbose", description: "Enable verbose logging");

    public CompareCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var compareCommand = new Command("compare", "Compare two GEDCOM files and output differences as JSON");

        compareCommand.AddOption(_configOption);
        compareCommand.AddOption(_sourceOption);
        compareCommand.AddOption(_destOption);
        compareCommand.AddOption(_anchorSourceOption);
        compareCommand.AddOption(_anchorDestOption);
        compareCommand.AddOption(_outputOption);
        compareCommand.AddOption(_depthOption);
        compareCommand.AddOption(_thresholdOption);
        compareCommand.AddOption(_includeDeletesOption);
        compareCommand.AddOption(_requireUniqueOption);
        compareCommand.AddOption(_verboseOption);

        compareCommand.SetHandler(HandleAsync);
        return compareCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var configPath = parseResult.GetValueForOption(_configOption);
        var sourcePath = parseResult.GetValueForOption(_sourceOption)!;
        var destPath = parseResult.GetValueForOption(_destOption)!;
        var anchorSource = parseResult.GetValueForOption(_anchorSourceOption)!;
        var anchorDest = parseResult.GetValueForOption(_anchorDestOption)!;
        var outputPath = parseResult.GetValueForOption(_outputOption);

        var overrides = new CompareOverrides(
            parseResult.GetValueForOption(_depthOption),
            parseResult.GetValueForOption(_thresholdOption),
            parseResult.GetValueForOption(_includeDeletesOption),
            parseResult.GetValueForOption(_requireUniqueOption),
            parseResult.GetValueForOption(_verboseOption));

        using var configScope = _startup.CreateScope(overrides.Verbose ?? false);
        var configurationService = configScope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var configuration = configurationService.LoadConfiguration(configPath);
        var settings = configurationService.BuildCompareSettings(configuration, overrides);

        await using var scope = _startup.CreateScope(settings.Verbose, services =>
        {
            services.AddSingleton(new MatchingOptions { MatchThreshold = settings.Threshold });
            services.AddSingleton<IPersonFieldComparer>(sp => new PersonFieldComparer(sp.GetRequiredService<ILogger<PersonFieldComparer>>()));
            services.AddSingleton<IFuzzyMatcherService>(sp => new FuzzyMatcherService(
                sp.GetRequiredService<INameVariantsService>(),
                sp.GetRequiredService<ILogger<FuzzyMatcherService>>(),
                sp.GetRequiredService<MatchingOptions>()));
            services.AddSingleton<IIndividualCompareService>(sp => new IndividualCompareService(
                sp.GetRequiredService<ILogger<IndividualCompareService>>(),
                sp.GetRequiredService<IPersonFieldComparer>(),
                sp.GetRequiredService<IFuzzyMatcherService>()));
            services.AddSingleton<IFamilyCompareService>(sp => new FamilyCompareService(
                sp.GetRequiredService<ILogger<FamilyCompareService>>(),
                sp.GetRequiredService<IFuzzyMatcherService>()));
            services.AddSingleton<IMappingValidationService>(sp => new MappingValidationService(
                sp.GetRequiredService<ILogger<MappingValidationService>>()));
            services.AddSingleton<IGedcomCompareService>(sp => new GedcomCompareService(
                sp.GetRequiredService<ILogger<GedcomCompareService>>(),
                sp.GetRequiredService<IGedcomLoader>(),
                sp.GetRequiredService<IIndividualCompareService>(),
                sp.GetRequiredService<IFamilyCompareService>(),
                sp.GetRequiredService<IMappingValidationService>()));
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Compare");

        try
        {
            logger.LogInformation("Configuration source: {Config}", string.IsNullOrEmpty(configPath) ? "auto-detected defaults" : configPath);
            logger.LogInformation("=== GEDCOM Compare ===");
            logger.LogInformation("Source: {Source}", sourcePath);
            logger.LogInformation("Destination: {Dest}", destPath);
            logger.LogInformation("Anchor Source: {AnchorSource}", anchorSource);
            logger.LogInformation("Anchor Dest: {AnchorDest}", anchorDest);
            logger.LogInformation("Depth: {Depth}, Threshold: {Threshold}%, Include Deletes: {IncludeDeletes}, Require Unique: {RequireUnique}",
                settings.NewNodeDepth, settings.Threshold, settings.IncludeDeletes, settings.RequireUnique);

            var compareService = provider.GetRequiredService<IGedcomCompareService>();

            var options = new CompareOptions
            {
                AnchorSourceId = GedcomIdNormalizer.Normalize(anchorSource),
                AnchorDestinationId = GedcomIdNormalizer.Normalize(anchorDest),
                NewNodeDepth = settings.NewNodeDepth,
                MatchThreshold = settings.Threshold,
                IncludeDeleteSuggestions = settings.IncludeDeletes,
                RequireUniqueMatch = settings.RequireUnique
            };

            var result = compareService.Compare(sourcePath, destPath, options);

            var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, json);
                logger.LogInformation("Comparison result saved to: {Path}", outputPath);
            }
            else
            {
                Console.WriteLine(json);
            }

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compare failed");
            context.ExitCode = 1;
        }
    }
}
