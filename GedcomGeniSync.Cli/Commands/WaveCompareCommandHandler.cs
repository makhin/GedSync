using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class WaveCompareCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _sourceOption = new("--source", description: "Path to source GEDCOM file") { IsRequired = true };
    private readonly Option<string> _destOption = new("--destination", description: "Path to destination GEDCOM file") { IsRequired = true };
    private readonly Option<string> _anchorSourceOption = new("--anchor-source", description: "GEDCOM ID of anchor person in source (e.g., @I123@)") { IsRequired = true };
    private readonly Option<string> _anchorDestOption = new("--anchor-destination", description: "GEDCOM ID of anchor person in destination (e.g., @I456@)") { IsRequired = true };
    private readonly Option<int> _maxLevelOption = new("--max-level", () => 10, description: "Maximum BFS level depth (default: 10)");
    private readonly Option<string> _thresholdStrategyOption = new("--threshold-strategy", () => "adaptive", description: "Threshold strategy: fixed, adaptive, aggressive, conservative");
    private readonly Option<int> _baseThresholdOption = new("--base-threshold", () => 60, description: "Base matching threshold (0-100, default: 60)");
    private readonly Option<string?> _outputOption = new("--output", description: "Output JSON file path (default: stdout)");
    private readonly Option<string?> _detailedLogOption = new("--detailed-log", description: "Output detailed text log file path (optional)");
    private readonly Option<bool> _verboseOption = new("--verbose", () => false, description: "Enable verbose logging");
    private readonly Option<bool> _ignorePhotosOption = new("--ignore-photos", () => false, description: "Ignore photo differences when comparing");

    public WaveCompareCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var waveCompareCommand = new Command("wave-compare", "Compare two GEDCOM files using wave algorithm and output detailed analysis");

        waveCompareCommand.AddOption(_sourceOption);
        waveCompareCommand.AddOption(_destOption);
        waveCompareCommand.AddOption(_anchorSourceOption);
        waveCompareCommand.AddOption(_anchorDestOption);
        waveCompareCommand.AddOption(_maxLevelOption);
        waveCompareCommand.AddOption(_thresholdStrategyOption);
        waveCompareCommand.AddOption(_baseThresholdOption);
        waveCompareCommand.AddOption(_outputOption);
        waveCompareCommand.AddOption(_detailedLogOption);
        waveCompareCommand.AddOption(_verboseOption);
        waveCompareCommand.AddOption(_ignorePhotosOption);

        waveCompareCommand.SetHandler(HandleAsync);
        return waveCompareCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var sourcePath = parseResult.GetValueForOption(_sourceOption)!;
        var destPath = parseResult.GetValueForOption(_destOption)!;
        var anchorSource = parseResult.GetValueForOption(_anchorSourceOption)!;
        var anchorDest = parseResult.GetValueForOption(_anchorDestOption)!;
        var maxLevel = parseResult.GetValueForOption(_maxLevelOption);
        var thresholdStrategyStr = parseResult.GetValueForOption(_thresholdStrategyOption)!;
        var baseThreshold = parseResult.GetValueForOption(_baseThresholdOption);
        var outputPath = parseResult.GetValueForOption(_outputOption);
        var detailedLogPath = parseResult.GetValueForOption(_detailedLogOption);
        var verbose = parseResult.GetValueForOption(_verboseOption);
        var ignorePhotos = parseResult.GetValueForOption(_ignorePhotosOption);

        // Build set of fields to ignore
        var fieldsToIgnore = new HashSet<string>();
        if (ignorePhotos)
        {
            fieldsToIgnore.Add("PhotoUrl");
        }

        await using var scope = _startup.CreateScope(verbose, services =>
        {
            services.AddSingleton<IFuzzyMatcherService>(sp => new FuzzyMatcherService(
                sp.GetRequiredService<INameVariantsService>(),
                sp.GetRequiredService<ILogger<FuzzyMatcherService>>(),
                new MatchingOptions()));
            services.AddSingleton<IPersonFieldComparer>(sp => new PersonFieldComparer(
                sp.GetRequiredService<ILogger<PersonFieldComparer>>(),
                fieldsToIgnore));
            services.AddSingleton<GedcomGeniSync.Core.Services.Wave.WaveCompareService>(sp =>
                new GedcomGeniSync.Core.Services.Wave.WaveCompareService(
                    sp.GetRequiredService<IFuzzyMatcherService>(),
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Core.Services.Wave.WaveCompareService>>()));
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("WaveCompare");

        try
        {
            logger.LogInformation("=== Wave Compare ===");
            logger.LogInformation("Source: {Source}", sourcePath);
            logger.LogInformation("Destination: {Dest}", destPath);
            logger.LogInformation("Anchor Source: {AnchorSource}", anchorSource);
            logger.LogInformation("Anchor Dest: {AnchorDest}", anchorDest);
            logger.LogInformation("Max Level: {MaxLevel}, Strategy: {Strategy}, Base Threshold: {Threshold}",
                maxLevel, thresholdStrategyStr, baseThreshold);

            var loader = provider.GetRequiredService<IGedcomLoader>();
            logger.LogInformation("Loading source GEDCOM...");
            var sourceLoadResult = loader.Load(sourcePath);
            logger.LogInformation("Loading destination GEDCOM...");
            var destLoadResult = loader.Load(destPath);

            var normalizedAnchorSource = GedcomIdNormalizer.Normalize(anchorSource);
            var normalizedAnchorDest = GedcomIdNormalizer.Normalize(anchorDest);

            var thresholdStrategy = thresholdStrategyStr.ToLowerInvariant() switch
            {
                "fixed" => GedcomGeniSync.Core.Models.Wave.ThresholdStrategy.Fixed,
                "adaptive" => GedcomGeniSync.Core.Models.Wave.ThresholdStrategy.Adaptive,
                "aggressive" => GedcomGeniSync.Core.Models.Wave.ThresholdStrategy.Aggressive,
                "conservative" => GedcomGeniSync.Core.Models.Wave.ThresholdStrategy.Conservative,
                _ => GedcomGeniSync.Core.Models.Wave.ThresholdStrategy.Adaptive
            };

            var options = new GedcomGeniSync.Core.Models.Wave.WaveCompareOptions
            {
                MaxLevel = maxLevel,
                ThresholdStrategy = thresholdStrategy,
                BaseThreshold = baseThreshold
            };

            var waveCompare = provider.GetRequiredService<GedcomGeniSync.Core.Services.Wave.WaveCompareService>();
            logger.LogInformation("Starting wave compare algorithm...");
            var result = waveCompare.Compare(
                sourceLoadResult,
                destLoadResult,
                normalizedAnchorSource,
                normalizedAnchorDest,
                options);

            logger.LogInformation("Wave compare completed!");
            logger.LogInformation("Mapped: {Mapped}/{Total} persons",
                result.Statistics.TotalMappings,
                result.Statistics.TotalSourcePersons);
            logger.LogInformation("Unmatched Source: {UnmatchedSource}",
                result.Statistics.UnmatchedSourceCount);
            logger.LogInformation("Unmatched Destination: {UnmatchedDest}",
                result.Statistics.UnmatchedDestinationCount);
            logger.LogInformation("Validation Issues: {Issues}",
                result.Statistics.ValidationIssuesCount);

            const int highConfidenceThreshold = 90;
            var fieldComparer = provider.GetRequiredService<IPersonFieldComparer>();
            var highConfidenceReport = BuildWaveReport(
                result,
                sourcePath,
                destPath,
                sourceLoadResult,
                destLoadResult,
                fieldComparer,
                highConfidenceThreshold);

            logger.LogInformation(
                "High-confidence report prepared: {ToUpdate} updates, {ToAdd} additions",
                highConfidenceReport.Individuals.NodesToUpdate.Count,
                highConfidenceReport.Individuals.NodesToAdd.Count);

            var payload = new
            {
                Summary = new
                {
                    Source = sourcePath,
                    Destination = destPath,
                    HighConfidenceThreshold = highConfidenceThreshold
                },
                Report = highConfidenceReport,
                WaveResult = result
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, json);
                logger.LogInformation("Result saved to: {Path}", outputPath);
            }
            else
            {
                Console.WriteLine(json);
            }

            if (!string.IsNullOrEmpty(detailedLogPath))
            {
                var detailedLog = waveCompare.GetDetailedLog();
                if (detailedLog != null)
                {
                    var formatter = new GedcomGeniSync.Core.Services.Wave.WaveCompareLogFormatter();
                    var formattedLog = formatter.FormatLog(detailedLog);
                    await File.WriteAllTextAsync(detailedLogPath, formattedLog);
                    logger.LogInformation("Detailed log saved to: {Path}", detailedLogPath);
                }
                else
                {
                    logger.LogWarning("No detailed log available");
                }
            }

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wave compare failed");
            context.ExitCode = 1;
        }
    }

    private static GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport BuildWaveReport(
        GedcomGeniSync.Core.Models.Wave.WaveCompareResult result,
        string sourceFile,
        string destinationFile,
        GedcomLoadResult sourceLoadResult,
        GedcomLoadResult destLoadResult,
        IPersonFieldComparer fieldComparer,
        int confidenceThreshold)
    {
        var mappingBySource = result.Mappings.ToDictionary(m => m.SourceId);

        var updates = ImmutableList.CreateBuilder<NodeToUpdate>();
        foreach (var mapping in mappingBySource.Values.Where(m => m.MatchScore >= confidenceThreshold))
        {
            if (!sourceLoadResult.Persons.TryGetValue(mapping.SourceId, out var sourcePerson)
                || !destLoadResult.Persons.TryGetValue(mapping.DestinationId, out var destPerson))
            {
                continue;
            }

            var differences = fieldComparer.CompareFields(sourcePerson, destPerson);
            if (differences.Any())
            {
                updates.Add(new NodeToUpdate
                {
                    SourceId = mapping.SourceId,
                    DestinationId = mapping.DestinationId,
                    GeniProfileId = destPerson.GeniProfileId,
                    MatchScore = mapping.MatchScore,
                    MatchedBy = mapping.FoundVia.ToString(),
                    PersonSummary = sourcePerson.ToString(),
                    FieldsToUpdate = differences
                });
            }
        }

        var additions = ImmutableList.CreateBuilder<NodeToAdd>();
        foreach (var unmatched in result.UnmatchedSource)
        {
            if (!sourceLoadResult.Persons.TryGetValue(unmatched.Id, out var sourcePerson))
            {
                continue;
            }

            var relation = FindHighConfidenceRelation(sourcePerson, mappingBySource, confidenceThreshold);
            if (relation == null)
            {
                continue;
            }

            additions.Add(new NodeToAdd
            {
                SourceId = sourcePerson.Id,
                PersonData = ConvertToPersonData(sourcePerson),
                RelatedToNodeId = relation.Value.RelatedSourceId,
                RelationType = relation.Value.RelationType,
                DepthFromExisting = unmatched.NearestMatchedLevel ?? 1
            });
        }

        return new GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport
        {
            SourceFile = sourceFile,
            DestinationFile = destinationFile,
            Anchors = result.Anchors,
            Options = result.Options,
            Individuals = new GedcomGeniSync.Core.Models.Wave.WaveIndividualsReport
            {
                NodesToUpdate = updates.ToImmutable(),
                NodesToAdd = additions.ToImmutable()
            }
        };
    }

    private static (string RelatedSourceId, CompareRelationType RelationType)? FindHighConfidenceRelation(
        PersonRecord person,
        IReadOnlyDictionary<string, GedcomGeniSync.Core.Models.Wave.PersonMapping> mappingBySource,
        int confidenceThreshold)
    {
        bool HasHighConfidence(string? relativeId) =>
            !string.IsNullOrWhiteSpace(relativeId)
            && mappingBySource.TryGetValue(relativeId!, out var mapping)
            && mapping.MatchScore >= confidenceThreshold;

        if (person.SpouseIds.FirstOrDefault(HasHighConfidence) is { } spouseId)
        {
            return (spouseId, CompareRelationType.Spouse);
        }

        if (HasHighConfidence(person.FatherId))
        {
            return (person.FatherId!, CompareRelationType.Child);
        }

        if (HasHighConfidence(person.MotherId))
        {
            return (person.MotherId!, CompareRelationType.Child);
        }

        var childRelation = person.ChildrenIds.FirstOrDefault(HasHighConfidence);
        if (!string.IsNullOrEmpty(childRelation))
        {
            return (childRelation!, CompareRelationType.Parent);
        }

        var siblingRelation = person.SiblingIds.FirstOrDefault(HasHighConfidence);
        if (!string.IsNullOrEmpty(siblingRelation))
        {
            return (siblingRelation!, CompareRelationType.Sibling);
        }

        return null;
    }

    private static PersonData ConvertToPersonData(PersonRecord person)
    {
        return new PersonData
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,
            MiddleName = person.MiddleName,
            Suffix = person.Suffix,
            Nickname = person.Nickname,
            Gender = person.Gender.ToString(),
            BirthDate = person.BirthDate?.ToString(),
            BirthPlace = person.BirthPlace,
            DeathDate = person.DeathDate?.ToString(),
            DeathPlace = person.DeathPlace,
            BurialDate = person.BurialDate?.ToString(),
            BurialPlace = person.BurialPlace,
            PhotoUrl = person.PhotoUrls.FirstOrDefault(),
            Occupation = person.Occupation,
            ResidenceAddress = person.ResidenceAddress ?? person.FormattedResidence,
            Notes = person.Notes
        };
    }
}
