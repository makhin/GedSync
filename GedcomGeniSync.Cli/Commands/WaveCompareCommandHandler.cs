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
using GedcomGeniSync.Services.Photo;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GedcomGeniSync.Tests")]

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
    private readonly Option<string?> _confirmedMappingsOption = new("--confirmed-mappings", description: "Path to JSON file with user-confirmed mappings (optional)");

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
        waveCompareCommand.AddOption(_confirmedMappingsOption);

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
        var confirmedMappingsPath = parseResult.GetValueForOption(_confirmedMappingsOption);

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
                fieldsToIgnore,
                sp.GetService<IPhotoCompareService>()));
            services.AddSingleton<GedcomGeniSync.Core.Services.Interactive.ConfirmedMappingsStore>(sp =>
                new GedcomGeniSync.Core.Services.Interactive.ConfirmedMappingsStore(
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Core.Services.Interactive.ConfirmedMappingsStore>>()));
            services.AddSingleton<GedcomGeniSync.Core.Services.Interactive.IInteractiveConfirmation>(sp =>
                new GedcomGeniSync.Core.Services.Interactive.ConsoleConfirmationService(
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Core.Services.Interactive.ConsoleConfirmationService>>()));
            services.AddSingleton<GedcomGeniSync.Core.Services.Wave.WaveCompareService>(sp =>
                new GedcomGeniSync.Core.Services.Wave.WaveCompareService(
                    sp.GetRequiredService<IFuzzyMatcherService>(),
                    sp.GetRequiredService<ILogger<GedcomGeniSync.Core.Services.Wave.WaveCompareService>>(),
                    sp.GetRequiredService<GedcomGeniSync.Core.Services.Interactive.ConfirmedMappingsStore>(),
                    sp.GetRequiredService<GedcomGeniSync.Core.Services.Interactive.IInteractiveConfirmation>()));
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
            var sourceLoadResult = await loader.LoadAsync(sourcePath, downloadPhotos: !ignorePhotos);
            logger.LogInformation("Loading destination GEDCOM...");
            var destLoadResult = await loader.LoadAsync(destPath, downloadPhotos: !ignorePhotos);

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

            // Load configuration for interactive options
            var configService = provider.GetRequiredService<IConfigurationService>();
            var config = configService.LoadConfiguration(null);

            var options = new GedcomGeniSync.Core.Models.Wave.WaveCompareOptions
            {
                MaxLevel = maxLevel,
                ThresholdStrategy = thresholdStrategy,
                BaseThreshold = baseThreshold,
                ConfirmedMappingsFile = confirmedMappingsPath ?? config.Interactive.ConfirmedMappingsFile,
                Interactive = config.Interactive.Enabled,
                LowConfidenceThreshold = config.Interactive.LowConfidenceThreshold,
                MinConfidenceThreshold = config.Interactive.MinConfidenceThreshold,
                MaxCandidates = config.Interactive.MaxCandidates,
                ResolveConflicts = config.WaveCompare.ResolveConflicts
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

            // Use interactive lowConfidenceThreshold for high-confidence relations
            // This ensures consistency: if we trust a match enough to auto-accept it,
            // we should also trust it enough to include in additionalRelations
            int highConfidenceThreshold = config.Interactive.LowConfidenceThreshold;
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

            // Check API for duplicates if enabled
            if (config.WaveCompare.CheckApiBeforeAdd)
            {
                var mappingBySource = result.Mappings.ToDictionary(m => m.SourceId, m => m);
                highConfidenceReport = await FilterDuplicatesViaApiAsync(
                    highConfidenceReport,
                    mappingBySource,
                    config,
                    provider,
                    logger);
            }

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

    internal static GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport BuildWaveReport(
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

            var relations = FindHighConfidenceRelations(sourcePerson, mappingBySource, confidenceThreshold);
            if (relations.Count == 0)
            {
                continue;
            }

            // First relation becomes the primary relation (for backward compatibility)
            var primaryRelation = relations[0];

            // Remaining relations become additional relations
            var additionalRelations = relations.Count > 1
                ? relations.Skip(1).Select(r => new AdditionalRelation
                {
                    RelatedToNodeId = r.RelatedSourceId,
                    RelationType = r.RelationType
                }).ToImmutableList()
                : ImmutableList<AdditionalRelation>.Empty;

            // Find the source family ID for this person (for child relations to find union)
            string? sourceFamilyId = null;
            if (primaryRelation.RelationType == CompareRelationType.Child && sourcePerson.ChildOfFamilyIds.Count > 0)
            {
                // Use the first family where this person is a child
                sourceFamilyId = sourcePerson.ChildOfFamilyIds[0];
            }

            // Skip profiles without first name AND last name - they cannot be meaningfully transferred
            if (string.IsNullOrWhiteSpace(sourcePerson.FirstName) && string.IsNullOrWhiteSpace(sourcePerson.LastName))
            {
                // Skip silently at this stage - will be logged in AddExecutor if needed
                continue;
            }

            additions.Add(new NodeToAdd
            {
                SourceId = sourcePerson.Id,
                PersonData = ConvertToPersonData(sourcePerson),
                RelatedToNodeId = primaryRelation.RelatedSourceId,
                RelationType = primaryRelation.RelationType,
                AdditionalRelations = additionalRelations,
                SourceFamilyId = sourceFamilyId,
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

    /// <summary>
    /// Finds all high-confidence relations for a person (not just the first one)
    /// </summary>
    private static ImmutableList<RelationInfo> FindHighConfidenceRelations(
        PersonRecord person,
        IReadOnlyDictionary<string, GedcomGeniSync.Core.Models.Wave.PersonMapping> mappingBySource,
        int confidenceThreshold)
    {
        var relations = ImmutableList.CreateBuilder<RelationInfo>();

        bool HasHighConfidence(string? relativeId) =>
            !string.IsNullOrWhiteSpace(relativeId)
            && mappingBySource.TryGetValue(relativeId!, out var mapping)
            && mapping.MatchScore >= confidenceThreshold;

        // Priority 1: Spouses - collect ALL spouses with high confidence
        foreach (var spouseId in person.SpouseIds.Where(HasHighConfidence))
        {
            relations.Add(new RelationInfo(spouseId, CompareRelationType.Spouse));
        }

        // Priority 2: Parents - collect BOTH parents if available
        if (HasHighConfidence(person.FatherId))
        {
            relations.Add(new RelationInfo(person.FatherId!, CompareRelationType.Child));
        }

        if (HasHighConfidence(person.MotherId))
        {
            relations.Add(new RelationInfo(person.MotherId!, CompareRelationType.Child));
        }

        // Priority 3: Children - collect ALL children with high confidence
        foreach (var childId in person.ChildrenIds.Where(HasHighConfidence))
        {
            relations.Add(new RelationInfo(childId, CompareRelationType.Parent));
        }

        // Priority 4: Siblings - only if no other relations found
        if (relations.Count == 0)
        {
            foreach (var siblingId in person.SiblingIds.Where(HasHighConfidence))
            {
                relations.Add(new RelationInfo(siblingId, CompareRelationType.Sibling));
            }
        }

        return relations.ToImmutable();
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns first relation only
    /// </summary>
    private static (string RelatedSourceId, CompareRelationType RelationType)? FindHighConfidenceRelation(
        PersonRecord person,
        IReadOnlyDictionary<string, GedcomGeniSync.Core.Models.Wave.PersonMapping> mappingBySource,
        int confidenceThreshold)
    {
        var relations = FindHighConfidenceRelations(person, mappingBySource, confidenceThreshold);
        return relations.Count > 0
            ? (relations[0].RelatedSourceId, relations[0].RelationType)
            : null;
    }

    /// <summary>
    /// Information about a relation to an existing person
    /// </summary>
    private record RelationInfo(string RelatedSourceId, CompareRelationType RelationType);

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

    /// <summary>
    /// Filter out profiles that already exist on Geni by checking via API
    /// </summary>
    private static async Task<GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport> FilterDuplicatesViaApiAsync(
        GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport report,
        IReadOnlyDictionary<string, GedcomGeniSync.Core.Models.Wave.PersonMapping> mappingBySource,
        GedSyncConfiguration config,
        IServiceProvider provider,
        ILogger logger)
    {
        var apiClient = provider.GetRequiredService<GedcomGeniSync.ApiClient.Services.Interfaces.IGeniProfileClient>();
        var duplicateChecker = new Services.ApiDuplicateChecker(
            apiClient,
            provider.GetRequiredService<ILogger<Services.ApiDuplicateChecker>>(),
            config.WaveCompare.ApiDuplicatesCacheFile);

        logger.LogInformation("Checking {Count} profiles via Geni API for duplicates...",
            report.Individuals.NodesToAdd.Count);

        var filteredNodes = new List<NodeToAdd>();
        var foundInApi = new List<GedcomGeniSync.Core.Models.Wave.ApiFoundProfile>();

        foreach (var nodeToAdd in report.Individuals.NodesToAdd)
        {
            // Get Geni profile ID of the relative
            if (!mappingBySource.TryGetValue(nodeToAdd.RelatedToNodeId, out var mapping))
            {
                logger.LogWarning(
                    "Cannot verify {SourceId} - related profile {RelatedId} not found in mappings",
                    nodeToAdd.SourceId,
                    nodeToAdd.RelatedToNodeId);
                filteredNodes.Add(nodeToAdd); // Keep if we can't verify
                continue;
            }

            var geniProfileId = NormalizeProfileId(mapping.DestinationId);
            if (string.IsNullOrEmpty(geniProfileId))
            {
                logger.LogWarning(
                    "Cannot verify {SourceId} - invalid Geni ID for related profile",
                    nodeToAdd.SourceId);
                filteredNodes.Add(nodeToAdd); // Keep if we can't verify
                continue;
            }

            // Check for duplicate via API
            var duplicateResult = await duplicateChecker.CheckForDuplicateAsync(nodeToAdd, geniProfileId);
            if (duplicateResult != null)
            {
                logger.LogInformation(
                    "Skipping {SourceId} ({Name}) - duplicate found on Geni: {GeniId}",
                    nodeToAdd.SourceId,
                    $"{nodeToAdd.PersonData.FirstName} {nodeToAdd.PersonData.LastName}",
                    duplicateResult.GeniProfileId);

                // Add to list of profiles found in API
                foundInApi.Add(new GedcomGeniSync.Core.Models.Wave.ApiFoundProfile
                {
                    SourceId = nodeToAdd.SourceId,
                    SourcePersonSummary = CreatePersonSummary(nodeToAdd.PersonData),
                    GeniProfileId = duplicateResult.GeniProfileId,
                    GeniProfileName = duplicateResult.GeniProfileName,
                    GeniProfileUrl = duplicateResult.GeniProfileUrl,
                    RelationType = nodeToAdd.RelationType?.ToString(),
                    FoundViaGeniId = duplicateResult.FoundViaGeniId
                });
            }
            else
            {
                filteredNodes.Add(nodeToAdd);
            }
        }

        logger.LogInformation(
            "API duplicate check complete: {Kept} profiles kept, {Filtered} duplicates filtered out",
            filteredNodes.Count,
            foundInApi.Count);

        // Return updated report with filtered nodes and found profiles
        return report with
        {
            Individuals = report.Individuals with
            {
                NodesToAdd = filteredNodes.ToImmutableList(),
                ProfilesFoundInApi = foundInApi.ToImmutableList()
            }
        };
    }

    /// <summary>
    /// Create a person summary string from PersonData
    /// </summary>
    private static string CreatePersonSummary(PersonData personData)
    {
        var name = $"{personData.FirstName} {personData.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "(no name)";

        var parts = new List<string> { name };

        if (personData.BirthDate != null || personData.BirthPlace != null)
        {
            var birth = personData.BirthDate ?? personData.BirthPlace ?? "";
            parts.Add($"b. {birth}");
        }

        if (personData.DeathDate != null || personData.DeathPlace != null)
        {
            var death = personData.DeathDate ?? personData.DeathPlace ?? "";
            parts.Add($"d. {death}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Normalize Geni profile ID from various formats to just the numeric ID
    /// </summary>
    private static string NormalizeProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return string.Empty;

        // Remove "geni:" prefix if present
        var normalized = profileId.Replace("geni:", "", StringComparison.OrdinalIgnoreCase);

        // Remove @ symbols if present (from GEDCOM IDs)
        normalized = normalized.Replace("@", "").Replace("I", "");

        return normalized;
    }
}
