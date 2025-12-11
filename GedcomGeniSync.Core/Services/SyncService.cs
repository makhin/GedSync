using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace GedcomGeniSync.Services;

/// <summary>
/// Context object for relative processing to reduce parameter count
/// </summary>
internal class RelativeProcessingContext
{
    public required string RelativeGedId { get; init; }
    public required string CurrentGeniId { get; init; }
    public required RelationType RelationType { get; init; }
    public required Gender ExpectedGender { get; init; }
    public required GeniImmediateFamily? GeniFamily { get; init; }
    public required GedcomLoadResult GedcomData { get; init; }
    public required Queue<(string GedcomId, string GeniId, int Depth)> Queue { get; init; }
    public required int CurrentDepth { get; init; }
    public PersonRecord? RelativePerson { get; set; }
}

/// <summary>
/// Orchestrates synchronization from GEDCOM to Geni
/// </summary>
[ExcludeFromCodeCoverage]
public class SyncService : ISyncService
{
    private readonly IGedcomLoader _gedcomLoader;
    private readonly IGeniApiClient _geniClient;
    private readonly IFuzzyMatcherService _matcher;
    private readonly IMyHeritagePhotoService? _photoService;
    private readonly ISyncStateManager _stateManager;
    private readonly ILogger<SyncService> _logger;
    private readonly SyncOptions _options;

    // State
    private readonly List<SyncResult> _results = new();
    private readonly SyncStatistics _statistics = new();

    public SyncService(
        IGedcomLoader gedcomLoader,
        IGeniApiClient geniClient,
        IFuzzyMatcherService matcher,
        ISyncStateManager stateManager,
        ILogger<SyncService> logger,
        SyncOptions? options = null,
        IMyHeritagePhotoService? photoService = null)
    {
        _gedcomLoader = gedcomLoader;
        _geniClient = geniClient;
        _matcher = matcher;
        _stateManager = stateManager;
        _logger = logger;
        _options = options ?? new SyncOptions();
        _photoService = photoService;
    }

    /// <summary>
    /// Run synchronization from GEDCOM file to Geni
    /// </summary>
    public async Task<SyncReport> SyncAsync(
        string gedcomPath,
        string anchorGedcomId,
        string anchorGeniId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync: {GedcomPath}", gedcomPath);
        _logger.LogInformation("Anchor: GEDCOM {GedId} → Geni {GeniId}", anchorGedcomId, anchorGeniId);
        _statistics.StartedAt = DateTime.UtcNow;
        _logger.LogDebug("Options: maxDepth={MaxDepth}, dryRun={DryRun}, stateFile={StateFile}, syncPhotos={SyncPhotos}",
            _options.MaxDepth, _options.DryRun, _options.StateFilePath, _options.SyncPhotos);

        // Load GEDCOM
        var gedcomData = _gedcomLoader.Load(gedcomPath);
        gedcomData.PrintStats(_logger);
        _statistics.GedcomPersonsTotal = gedcomData.TotalPersons;
        _statistics.GedcomFamiliesTotal = gedcomData.TotalFamilies;

        // Normalize anchor ID to standard GEDCOM format with @ delimiters
        var normalizedAnchorId = GedcomIdNormalizer.Normalize(anchorGedcomId);

        // Verify anchor exists in GEDCOM
        var anchorPerson = gedcomData.Persons.GetValueOrDefault(normalizedAnchorId);

        if (anchorPerson == null)
        {
            // Debug: list available IDs to help user
            var availableIds = gedcomData.Persons.Keys
                .Where(k => k.Contains("500002", StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            var debugInfo = availableIds.Any()
                ? $"Similar IDs found: {string.Join(", ", availableIds)}"
                : $"First 5 IDs in GEDCOM: {string.Join(", ", gedcomData.Persons.Keys.Take(5))}";

            throw new ArgumentException(
                $"Anchor person '{anchorGedcomId}' (normalized: '{normalizedAnchorId}') not found in GEDCOM file. {debugInfo}");
        }

        // Verify anchor exists in Geni
        var anchorGeniProfile = await _geniClient.GetProfileAsync(anchorGeniId);
        if (anchorGeniProfile == null)
        {
            throw new ArgumentException($"Anchor profile {anchorGeniId} not found in Geni");
        }

        _logger.LogInformation("Anchor verified: {Name} in GEDCOM, {GeniName} in Geni",
            anchorPerson.FullName,
            $"{anchorGeniProfile.FirstName} {anchorGeniProfile.LastName}");

        // Use the resolved internal ID (anchorPerson.Id) for all mappings
        var anchorInternalId = anchorPerson.Id;

        // Initialize mapping with anchor
        _stateManager.AddMapping(anchorInternalId, anchorGeniId);
        _stateManager.MarkAsProcessed(anchorInternalId);

        _statistics.ProfilesMatched++;
        _statistics.QueueEnqueued++;

        _results.Add(new SyncResult
        {
            GedcomId = anchorGedcomId, // Keep original ID for user reporting
            GeniId = anchorGeniId,
            PersonName = anchorPerson.FullName,
            Action = SyncAction.Matched,
            MatchScore = 100
        });

        // Load existing state if resuming
        if (!string.IsNullOrEmpty(_options.StateFilePath) && File.Exists(_options.StateFilePath))
        {
            await LoadStateAsync();
        }

        // BFS from anchor - use internal ID
        var queue = new Queue<(string GedcomId, string GeniId, int Depth)>();
        queue.Enqueue((anchorInternalId, anchorGeniId, 0));

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentGedId, currentGeniId, depth) = queue.Dequeue();

            _statistics.QueueDequeued++;
            _logger.LogDebug("Dequeued GEDCOM:{GedId} / Geni:{GeniId} at depth {Depth}", currentGedId, currentGeniId, depth);

            if (_options.MaxDepth.HasValue && depth >= _options.MaxDepth.Value)
            {
                _logger.LogDebug("Skipping descendants beyond max depth for {GedId}", currentGedId);
                continue;
            }

            var currentPerson = gedcomData.Persons.GetValueOrDefault(currentGedId);
            if (currentPerson == null) continue;

            _logger.LogDebug("Processing: {Name} (depth {Depth})", currentPerson.FullName, depth);

            // Get Geni family for current person
            _statistics.GeniFamilyRequests++;
            _logger.LogInformation("Requesting immediate family for {GeniId}", currentGeniId);
            var geniFamily = await _geniClient.GetImmediateFamilyAsync(currentGeniId);

            // Process all relatives
            await ProcessRelativesAsync(
                currentPerson,
                currentGeniId,
                geniFamily,
                gedcomData,
                queue,
                depth,
                cancellationToken);

            // Save state periodically
            if (_results.Count % 50 == 0 && !string.IsNullOrEmpty(_options.StateFilePath))
            {
                await SaveStateAsync();
            }
        }

        // Final state save
        if (!string.IsNullOrEmpty(_options.StateFilePath))
        {
            await SaveStateAsync();
        }

        _statistics.FinishedAt = DateTime.UtcNow;
        return GenerateReport();
    }

    private async Task ProcessRelativesAsync(
        PersonRecord currentPerson,
        string currentGeniId,
        GeniImmediateFamily? geniFamily,
        GedcomLoadResult gedcomData,
        Queue<(string GedcomId, string GeniId, int Depth)> queue,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        // Process parents
        if (!string.IsNullOrEmpty(currentPerson.FatherId))
        {
            await ProcessRelativeAsync(
                currentPerson.FatherId,
                currentGeniId,
                RelationType.Parent,
                Gender.Male,
                geniFamily,
                gedcomData,
                queue,
                currentDepth,
                cancellationToken);
        }

        if (!string.IsNullOrEmpty(currentPerson.MotherId))
        {
            await ProcessRelativeAsync(
                currentPerson.MotherId,
                currentGeniId,
                RelationType.Parent,
                Gender.Female,
                geniFamily,
                gedcomData,
                queue,
                currentDepth,
                cancellationToken);
        }

        // Process spouses
        foreach (var spouseId in currentPerson.SpouseIds)
        {
            await ProcessRelativeAsync(
                spouseId,
                currentGeniId,
                RelationType.Partner,
                Gender.Unknown,
                geniFamily,
                gedcomData,
                queue,
                currentDepth,
                cancellationToken);
        }

        // Process children
        foreach (var childId in currentPerson.ChildrenIds)
        {
            await ProcessRelativeAsync(
                childId,
                currentGeniId,
                RelationType.Child,
                Gender.Unknown,
                geniFamily,
                gedcomData,
                queue,
                currentDepth,
                cancellationToken);
        }
    }

    /// <summary>
    /// Validates relative data and determines if processing should continue
    /// </summary>
    /// <returns>True if validation passed and processing should continue</returns>
    private bool ValidateRelativeData(RelativeProcessingContext context)
    {
        // Check if relative exists in GEDCOM
        context.RelativePerson = context.GedcomData.Persons.GetValueOrDefault(context.RelativeGedId);
        if (context.RelativePerson == null)
        {
            _logger.LogWarning("Relative {Id} not found in GEDCOM", context.RelativeGedId);
            _statistics.ProfileErrors++;
            return false;
        }

        // Skip if insufficient data (no name)
        if (string.IsNullOrEmpty(context.RelativePerson.FirstName) &&
            string.IsNullOrEmpty(context.RelativePerson.LastName))
        {
            _statistics.ProfilesSkipped++;
            _results.Add(new SyncResult
            {
                GedcomId = context.RelativeGedId,
                PersonName = context.RelativePerson.FullName,
                Action = SyncAction.Skipped,
                ErrorMessage = "Insufficient data (no name)"
            });
            _logger.LogWarning("Skipping {RelType}: {Name} due to missing name data",
                context.RelationType, context.RelativePerson.FullName);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to match existing profile or creates a new one
    /// </summary>
    /// <returns>Geni ID of matched or created profile, or null if failed</returns>
    private async Task<string?> MatchOrCreateProfileAsync(RelativeProcessingContext context)
    {
        if (context.RelativePerson == null)
            return null;

        _logger.LogInformation("Processing {RelType}: {Name}", context.RelationType, context.RelativePerson.FullName);

        // Try to find match in Geni family
        var matchedGeniId = FindMatchInGeniFamily(
            context.RelativePerson,
            context.RelationType,
            context.ExpectedGender,
            context.GeniFamily);

        if (matchedGeniId != null)
        {
            // Found existing match
            _stateManager.AddMapping(context.RelativeGedId, matchedGeniId);

            var matchScore = CalculateMatchScore(context.RelativePerson, context.GeniFamily, matchedGeniId);

            _results.Add(new SyncResult
            {
                GedcomId = context.RelativeGedId,
                GeniId = matchedGeniId,
                PersonName = context.RelativePerson.FullName,
                Action = SyncAction.Matched,
                MatchScore = matchScore,
                RelationType = context.RelationType.ToString()
            });

            _statistics.ProfilesMatched++;

            _logger.LogInformation("MATCHED: {Name} → Geni:{GeniId} (score: {Score}%)",
                context.RelativePerson.FullName, matchedGeniId, matchScore);

            // Sync photos if enabled
            if (_options.SyncPhotos && _photoService != null)
            {
                await SyncPhotosAsync(context.RelativePerson, matchedGeniId);
            }

            return matchedGeniId;
        }
        else
        {
            // Need to create new profile
            var (createdProfile, errorMessage) = await CreateProfileAsync(
                context.RelativePerson,
                context.CurrentGeniId,
                context.RelationType);

            if (createdProfile != null)
            {
                var createdGeniId = createdProfile.NumericId;
                _stateManager.AddMapping(context.RelativeGedId, createdGeniId);

                _results.Add(new SyncResult
                {
                    GedcomId = context.RelativeGedId,
                    GeniId = createdGeniId,
                    PersonName = context.RelativePerson.FullName,
                    Action = SyncAction.Created,
                    RelationType = context.RelationType.ToString(),
                    RelativeGeniId = context.CurrentGeniId
                });

                _statistics.ProfilesCreated++;
                if (_options.DryRun)
                {
                    _statistics.DryRunProfileCreations++;
                }

                _logger.LogInformation("CREATED: {Name} → Geni:{GeniId} as {RelType} of {ParentId}",
                    context.RelativePerson.FullName, createdGeniId, context.RelationType, context.CurrentGeniId);

                // Sync photos if enabled
                if (_options.SyncPhotos && _photoService != null)
                {
                    await SyncPhotosAsync(context.RelativePerson, createdGeniId);
                }

                return createdGeniId;
            }
            else
            {
                _logger.LogWarning("Failed to create profile for {Name}: {Error}",
                    context.RelativePerson.FullName, errorMessage ?? "Unknown error");

                _results.Add(new SyncResult
                {
                    GedcomId = context.RelativeGedId,
                    PersonName = context.RelativePerson.FullName,
                    Action = SyncAction.Error,
                    RelationType = context.RelationType.ToString(),
                    ErrorMessage = errorMessage ?? "Failed to create profile"
                });

                _statistics.ProfileErrors++;
                return null;
            }
        }
    }

    /// <summary>
    /// Enqueues relative for further processing
    /// </summary>
    private void EnqueueRelative(RelativeProcessingContext context, string geniId)
    {
        context.Queue.Enqueue((context.RelativeGedId, geniId, context.CurrentDepth + 1));
        _statistics.QueueEnqueued++;
    }

    private async Task ProcessRelativeAsync(
        string relativeGedId,
        string currentGeniId,
        RelationType relationType,
        Gender expectedGender,
        GeniImmediateFamily? geniFamily,
        GedcomLoadResult gedcomData,
        Queue<(string GedcomId, string GeniId, int Depth)> queue,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        // Skip if already processed
        if (_stateManager.IsProcessed(relativeGedId))
        {
            _logger.LogDebug("Already processed relative {Id}, skipping", relativeGedId);
            return;
        }

        _stateManager.MarkAsProcessed(relativeGedId);

        // Create context object to encapsulate all parameters
        var context = new RelativeProcessingContext
        {
            RelativeGedId = relativeGedId,
            CurrentGeniId = currentGeniId,
            RelationType = relationType,
            ExpectedGender = expectedGender,
            GeniFamily = geniFamily,
            GedcomData = gedcomData,
            Queue = queue,
            CurrentDepth = currentDepth
        };

        // Validate relative data
        if (!ValidateRelativeData(context))
            return;

        // Match or create profile
        var geniId = await MatchOrCreateProfileAsync(context);

        // Enqueue for further processing if successful
        if (geniId != null)
        {
            EnqueueRelative(context, geniId);
        }
    }

    private string? FindMatchInGeniFamily(
        PersonRecord gedcomPerson,
        RelationType relationType,
        Gender expectedGender,
        GeniImmediateFamily? geniFamily)
    {
        _statistics.MatchAttempts++;
        if (geniFamily?.Nodes == null)
            return null;

        // Convert Geni nodes to PersonRecords for matching
        var candidates = new List<(string GeniId, PersonRecord Person)>();

        foreach (var (nodeId, node) in geniFamily.Nodes)
        {
            // Skip if already mapped
            if (_stateManager.IsMappedToGeni(nodeId))
                continue;

            // Filter by expected gender if known
            if (expectedGender != Gender.Unknown)
            {
                var nodeGender = node.Gender?.ToLowerInvariant() switch
                {
                    "male" => Gender.Male,
                    "female" => Gender.Female,
                    _ => Gender.Unknown
                };

                if (nodeGender != Gender.Unknown && nodeGender != expectedGender)
                    continue;
            }

            var candidatePerson = ConvertGeniNodeToPerson(nodeId, node);
            candidates.Add((nodeId, candidatePerson));
            _logger.LogDebug("Candidate for {Relation}: {Name} ({GeniId})", relationType, candidatePerson.FullName, nodeId);
        }

        if (candidates.Count == 0)
            return null;

        // Find best match
        var matches = _matcher.FindMatches(
            gedcomPerson,
            candidates.Select(c => c.Person),
            _options.MatchingOptions.MatchThreshold);

        _logger.LogDebug("Found {Count} matches above threshold {Threshold}% for {Name}",
            matches.Count, _options.MatchingOptions.MatchThreshold, gedcomPerson.FullName);

        // Log all match attempts for debugging
        foreach (var match in matches.Take(5))
        {
            var reasons = string.Join(", ", match.Reasons.Select(r => $"{r.Field}:{r.Points:F1}"));
            _logger.LogDebug("  Match candidate: {Name} - Score: {Score:F1}% ({Reasons})",
                match.Target.FullName, match.Score, reasons);
        }

        if (matches.Count == 0)
        {
            _logger.LogDebug("No matches found above threshold {Threshold}% for {Name}",
                _options.MatchingOptions.MatchThreshold, gedcomPerson.FullName);
            return null;
        }

        var bestMatch = matches[0];

        // Return Geni ID if match is good enough
        if (bestMatch.Score >= _options.MatchingOptions.MatchThreshold)
        {
            var matchedCandidate = candidates.First(c => c.Person.Id == bestMatch.Target.Id);
            _logger.LogInformation("Best match for {Name}: {CandidateName} ({Score}%)", gedcomPerson.FullName,
                matchedCandidate.Person.FullName, Math.Round(bestMatch.Score, 1));
            return matchedCandidate.GeniId;
        }

        _logger.LogInformation("No suitable match found for {Name}. Best score {Score}%", gedcomPerson.FullName,
            Math.Round(bestMatch.Score, 1));

        return null;
    }

    private PersonRecord ConvertGeniNodeToPerson(string geniId, GeniNode node)
    {
        // Log raw Geni data for debugging - include ALL name fields
        _logger.LogDebug("Converting Geni node {GeniId}: Name='{Name}', FirstName='{FirstName}', MiddleName='{MiddleName}', LastName='{LastName}', " +
            "MaidenName='{MaidenName}', Suffix='{Suffix}', Gender='{Gender}', BirthDate='{BirthDate}'",
            geniId,
            node.Name ?? "(null)",
            node.FirstName ?? "(null)",
            node.MiddleName ?? "(null)",
            node.LastName ?? "(null)",
            node.MaidenName ?? "(null)",
            node.Suffix ?? "(null)",
            node.Gender ?? "(null)",
            node.BirthDate ?? "(null)");

        // Extract multilingual name variants from Geni Names field
        var nameVariants = ExtractNameVariantsFromGeniNames(node.Names, node.FirstName, node.LastName);

        return new PersonRecord
        {
            Id = geniId,
            Source = PersonSource.Geni,
            FirstName = node.FirstName,
            MiddleName = node.MiddleName,
            LastName = node.LastName,
            MaidenName = node.MaidenName,
            NormalizedFirstName = NameNormalizer.Normalize(node.FirstName),
            NormalizedLastName = NameNormalizer.Normalize(node.LastName),
            NameVariants = nameVariants,
            Gender = node.Gender?.ToLowerInvariant() switch
            {
                "male" => Gender.Male,
                "female" => Gender.Female,
                _ => Gender.Unknown
            },
            BirthDate = DateInfo.Parse(node.BirthDate)
        };
    }

    /// <summary>
    /// Extract name variants from Geni Names field
    /// Names field structure: { "en": { "first_name": "John", "last_name": "Smith" }, "ru": { "first_name": "Иван", "last_name": "Смит" } }
    /// </summary>
    private ImmutableList<string> ExtractNameVariantsFromGeniNames(
        Dictionary<string, Dictionary<string, string>>? names,
        string? primaryFirstName,
        string? primaryLastName)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (names == null || names.Count == 0)
            return ImmutableList<string>.Empty;

        foreach (var (language, nameFields) in names)
        {
            // Extract first name variants
            if (nameFields.TryGetValue("first_name", out var firstName) && !string.IsNullOrWhiteSpace(firstName))
            {
                // Only add if different from primary first name
                if (!string.Equals(firstName, primaryFirstName, StringComparison.OrdinalIgnoreCase))
                {
                    variants.Add(firstName.Trim());
                }
            }

            // Extract middle name variants
            if (nameFields.TryGetValue("middle_name", out var middleName) && !string.IsNullOrWhiteSpace(middleName))
            {
                variants.Add(middleName.Trim());
            }

            // Extract last name variants (including maiden names from different languages)
            if (nameFields.TryGetValue("last_name", out var lastName) && !string.IsNullOrWhiteSpace(lastName))
            {
                // Only add if different from primary last name
                if (!string.Equals(lastName, primaryLastName, StringComparison.OrdinalIgnoreCase))
                {
                    variants.Add(lastName.Trim());
                }
            }

            // Extract maiden name variants
            if (nameFields.TryGetValue("maiden_name", out var maidenName) && !string.IsNullOrWhiteSpace(maidenName))
            {
                variants.Add(maidenName.Trim());
            }
        }

        _logger.LogDebug("Extracted {Count} name variants from Geni Names field", variants.Count);

        return variants.ToImmutableList();
    }

    private int CalculateMatchScore(
        PersonRecord gedcomPerson,
        GeniImmediateFamily? geniFamily,
        string geniId)
    {
        if (geniFamily?.Nodes == null || !geniFamily.Nodes.TryGetValue(geniId, out var node))
            return 0;

        var geniPerson = ConvertGeniNodeToPerson(geniId, node);
        var match = _matcher.Compare(gedcomPerson, geniPerson);
        return (int)Math.Round(match.Score);
    }

    private async Task<(GeniProfile? Profile, string? ErrorMessage)> CreateProfileAsync(
        PersonRecord person,
        string relativeGeniId,
        RelationType relationType)
    {
        var profileCreate = new GeniProfileCreate
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,
            Gender = person.Gender switch
            {
                Gender.Male => "male",
                Gender.Female => "female",
                _ => null
            },
            BirthDate = person.BirthDate?.ToGeniFormat(),
            BirthPlace = person.BirthPlace,
            DeathDate = person.DeathDate?.ToGeniFormat(),
            DeathPlace = person.DeathPlace
        };

        try
        {
            _logger.LogInformation("Creating {Relation} profile for {Name} (target Geni {GeniId})", relationType,
                person.FullName, relativeGeniId);
            var profile = relationType switch
            {
                RelationType.Parent => await _geniClient.AddParentAsync(relativeGeniId, profileCreate),
                RelationType.Child => await _geniClient.AddChildAsync(relativeGeniId, profileCreate),
                RelationType.Partner => await _geniClient.AddPartnerAsync(relativeGeniId, profileCreate),
                _ => null
            };
            return (profile, null);
        }
        catch (HttpRequestException ex)
        {
            var errorMsg = $"HTTP {ex.StatusCode}: {ex.Message}";
            _logger.LogError(ex, "Failed to create {RelType} for {Name} - {Error}",
                relationType, person.FullName, errorMsg);
            _statistics.ProfileErrors++;
            return (null, errorMsg);
        }
        catch (TaskCanceledException ex)
        {
            var errorMsg = "Request timeout";
            _logger.LogError(ex, "Timeout creating {RelType} for {Name}",
                relationType, person.FullName);
            _statistics.ProfileErrors++;
            return (null, errorMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "Failed to create {RelType} for {Name} - {Error}",
                relationType, person.FullName, errorMsg);
            _statistics.ProfileErrors++;
            return (null, errorMsg);
        }
    }

    #region State Persistence

    private async Task SaveStateAsync()
    {
        var state = new SyncState
        {
            GedcomToGeniMap = new Dictionary<string, string>(_stateManager.GetAllMappings()),
            ProcessedIds = _stateManager.GetProcessedIds().ToList(),
            Results = _results
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_options.StateFilePath!, json);

        _logger.LogInformation("State saved: {Count} mappings, {Processed} processed",
            _stateManager.GetAllMappings().Count, _stateManager.GetProcessedIds().Count);
    }

    private async Task LoadStateAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_options.StateFilePath!);
            var state = JsonSerializer.Deserialize<SyncState>(json);

            if (state != null)
            {
                _stateManager.LoadState(state.GedcomToGeniMap, state.ProcessedIds);

                _results.AddRange(state.Results);

                _statistics.ProfilesMatched = _results.Count(r => r.Action == SyncAction.Matched);
                _statistics.ProfilesCreated = _results.Count(r => r.Action == SyncAction.Created);
                _statistics.ProfilesSkipped = _results.Count(r => r.Action == SyncAction.Skipped);
                _statistics.ProfileErrors = _results.Count(r => r.Action == SyncAction.Error);
                _statistics.QueueEnqueued = _stateManager.GetProcessedIds().Count;
                _statistics.QueueDequeued = _stateManager.GetProcessedIds().Count;

                _logger.LogInformation("State loaded: {Count} mappings, {Processed} processed",
                    _stateManager.GetAllMappings().Count, _stateManager.GetProcessedIds().Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load state, starting fresh");
        }
    }

    #endregion

    #region Photo Synchronization

    /// <summary>
    /// Sync photos from GEDCOM to Geni profile
    /// Downloads MyHeritage photos and uploads to Geni if profile has no photos
    /// </summary>
    private async Task SyncPhotosAsync(PersonRecord gedcomPerson, string geniProfileId)
    {
        if (_photoService == null)
            return;

        // Skip if no photos in GEDCOM
        if (gedcomPerson.PhotoUrls.Count == 0)
        {
            _logger.LogDebug("No photos in GEDCOM for {Name}", gedcomPerson.FullName);
            return;
        }

        try
        {
            _logger.LogInformation("Syncing photos for {Name} into Geni profile {GeniId}", gedcomPerson.FullName, geniProfileId);
            // Check if profile already has photos
            var existingPhotos = await _geniClient.GetPhotosAsync(geniProfileId);

            if (existingPhotos.Count > 0)
            {
                _logger.LogInformation("Profile {GeniId} already has {Count} photo(s), skipping photo sync",
                    geniProfileId, existingPhotos.Count);
                return;
            }

            _logger.LogInformation("Profile {GeniId} has no photos, syncing from GEDCOM ({Count} URLs)",
                geniProfileId, gedcomPerson.PhotoUrls.Count);

            // Filter MyHeritage URLs
            var myHeritageUrls = gedcomPerson.PhotoUrls
                .Where(url => _photoService.IsMyHeritageUrl(url))
                .ToList();

            if (myHeritageUrls.Count == 0)
            {
                _logger.LogInformation("No MyHeritage photo URLs found for {Name}", gedcomPerson.FullName);
                return;
            }

            _logger.LogInformation("Found {Count} MyHeritage photo URL(s) for {Name}",
                myHeritageUrls.Count, gedcomPerson.FullName);

            // Download and upload photos
            foreach (var url in myHeritageUrls)
            {
                _statistics.PhotoDownloadAttempts++;
                var downloadResult = await _photoService.DownloadPhotoAsync(url);

                if (downloadResult == null)
                {
                    _logger.LogWarning("Failed to download photo from {Url}", url);
                    continue;
                }

                _logger.LogInformation("Downloaded photo from {Url} ({Size} bytes)",
                    url, downloadResult.Data.Length);

                // Upload to Geni
                var caption = $"Photo from MyHeritage (originally from GEDCOM)";
                var uploadedPhoto = await _geniClient.AddPhotoFromBytesAsync(
                    geniProfileId,
                    downloadResult.Data,
                    downloadResult.FileName,
                    caption);

                if (uploadedPhoto != null)
                {
                    _logger.LogInformation("Successfully uploaded photo {PhotoId} to profile {GeniId}",
                        uploadedPhoto.Id, geniProfileId);
                    _statistics.PhotoUploads++;

                    // Set as mugshot if it's the first photo
                    if (existingPhotos.Count == 0)
                    {
                        var success = await _geniClient.SetExistingPhotoAsMugshotAsync(
                            geniProfileId,
                            uploadedPhoto.NumericId);

                        if (success)
                        {
                            _logger.LogInformation("Set photo {PhotoId} as mugshot for profile {GeniId}",
                                uploadedPhoto.Id, geniProfileId);
                        }
                    }

                    existingPhotos.Add(uploadedPhoto);
                }
                else
                {
                    _logger.LogWarning("Failed to upload photo to profile {GeniId}", geniProfileId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing photos for {Name} (Geni: {GeniId})",
                gedcomPerson.FullName, geniProfileId);
        }
    }

    #endregion

    #region Reporting

    private SyncReport GenerateReport()
    {
        var report = new SyncReport
        {
            TotalProcessed = _results.Count,
            Matched = _results.Count(r => r.Action == SyncAction.Matched),
            Created = _results.Count(r => r.Action == SyncAction.Created),
            Skipped = _results.Count(r => r.Action == SyncAction.Skipped),
            Errors = _results.Count(r => r.Action == SyncAction.Error),
            Results = _results,
            GedcomToGeniMap = new Dictionary<string, string>(_stateManager.GetAllMappings()),
            Statistics = _statistics.Clone()
        };

        return report;
    }

    #endregion
}
