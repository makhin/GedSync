using System.Diagnostics.CodeAnalysis;
using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace GedcomGeniSync.Services;

/// <summary>
/// Orchestrates synchronization from GEDCOM to Geni
/// </summary>
[ExcludeFromCodeCoverage]
public class SyncService
{
    private readonly GedcomLoader _gedcomLoader;
    private readonly GeniApiClient _geniClient;
    private readonly FuzzyMatcherService _matcher;
    private readonly MyHeritagePhotoService? _photoService;
    private readonly ILogger<SyncService> _logger;
    private readonly SyncOptions _options;

    // State
    private readonly Dictionary<string, string> _gedcomToGeniMap = new(); // GED ID → Geni ID
    private readonly Dictionary<string, string> _geniToGedcomMap = new(); // Geni ID → GED ID
    private readonly HashSet<string> _processedGedcomIds = new();
    private readonly List<SyncResult> _results = new();

    public SyncService(
        GedcomLoader gedcomLoader,
        GeniApiClient geniClient,
        FuzzyMatcherService matcher,
        ILogger<SyncService> logger,
        SyncOptions? options = null,
        MyHeritagePhotoService? photoService = null)
    {
        _gedcomLoader = gedcomLoader;
        _geniClient = geniClient;
        _matcher = matcher;
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

        // Load GEDCOM
        var gedcomData = _gedcomLoader.Load(gedcomPath);
        gedcomData.PrintStats(_logger);

        // Verify anchor exists in GEDCOM
        var anchorPerson = gedcomData.Persons.GetValueOrDefault(anchorGedcomId);
        if (anchorPerson == null)
        {
            throw new ArgumentException($"Anchor person {anchorGedcomId} not found in GEDCOM file");
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

        // Initialize mapping with anchor
        _gedcomToGeniMap[anchorGedcomId] = anchorGeniId;
        _geniToGedcomMap[anchorGeniId] = anchorGedcomId;
        _processedGedcomIds.Add(anchorGedcomId);

        _results.Add(new SyncResult
        {
            GedcomId = anchorGedcomId,
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

        // BFS from anchor
        var queue = new Queue<(string GedcomId, string GeniId, int Depth)>();
        queue.Enqueue((anchorGedcomId, anchorGeniId, 0));

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentGedId, currentGeniId, depth) = queue.Dequeue();

            if (_options.MaxDepth.HasValue && depth >= _options.MaxDepth.Value)
            {
                continue;
            }

            var currentPerson = gedcomData.Persons.GetValueOrDefault(currentGedId);
            if (currentPerson == null) continue;

            _logger.LogDebug("Processing: {Name} (depth {Depth})", currentPerson.FullName, depth);

            // Get Geni family for current person
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
        if (_processedGedcomIds.Contains(relativeGedId))
            return;

        _processedGedcomIds.Add(relativeGedId);

        var relativePerson = gedcomData.Persons.GetValueOrDefault(relativeGedId);
        if (relativePerson == null)
        {
            _logger.LogWarning("Relative {Id} not found in GEDCOM", relativeGedId);
            return;
        }

        // Skip if insufficient data
        if (string.IsNullOrEmpty(relativePerson.FirstName) && 
            string.IsNullOrEmpty(relativePerson.LastName))
        {
            _results.Add(new SyncResult
            {
                GedcomId = relativeGedId,
                PersonName = relativePerson.FullName,
                Action = SyncAction.Skipped,
                ErrorMessage = "Insufficient data (no name)"
            });
            return;
        }

        _logger.LogInformation("Processing {RelType}: {Name}", relationType, relativePerson.FullName);

        // Try to find match in Geni family
        var matchedGeniId = await FindMatchInGeniFamilyAsync(
            relativePerson,
            relationType,
            expectedGender,
            geniFamily);

        if (matchedGeniId != null)
        {
            // Found existing match
            _gedcomToGeniMap[relativeGedId] = matchedGeniId;
            _geniToGedcomMap[matchedGeniId] = relativeGedId;

            var matchScore = CalculateMatchScore(relativePerson, geniFamily, matchedGeniId);

            _results.Add(new SyncResult
            {
                GedcomId = relativeGedId,
                GeniId = matchedGeniId,
                PersonName = relativePerson.FullName,
                Action = SyncAction.Matched,
                MatchScore = matchScore,
                RelationType = relationType.ToString()
            });

            _logger.LogInformation("MATCHED: {Name} → Geni:{GeniId} (score: {Score}%)",
                relativePerson.FullName, matchedGeniId, matchScore);

            // Sync photos if enabled
            if (_options.SyncPhotos && _photoService != null)
            {
                await SyncPhotosAsync(relativePerson, matchedGeniId);
            }

            // Add to queue for further processing
            queue.Enqueue((relativeGedId, matchedGeniId, currentDepth + 1));
        }
        else
        {
            // Need to create new profile
            var (createdProfile, errorMessage) = await CreateProfileAsync(
                relativePerson,
                currentGeniId,
                relationType);

            if (createdProfile != null)
            {
                var createdGeniId = createdProfile.NumericId;
                _gedcomToGeniMap[relativeGedId] = createdGeniId;
                _geniToGedcomMap[createdGeniId] = relativeGedId;

                _results.Add(new SyncResult
                {
                    GedcomId = relativeGedId,
                    GeniId = createdGeniId,
                    PersonName = relativePerson.FullName,
                    Action = SyncAction.Created,
                    RelationType = relationType.ToString(),
                    RelativeGeniId = currentGeniId
                });

                _logger.LogInformation("CREATED: {Name} → Geni:{GeniId} as {RelType} of {ParentId}",
                    relativePerson.FullName, createdGeniId, relationType, currentGeniId);

                // Sync photos if enabled
                if (_options.SyncPhotos && _photoService != null)
                {
                    await SyncPhotosAsync(relativePerson, createdGeniId);
                }

                // Add to queue for further processing
                queue.Enqueue((relativeGedId, createdGeniId, currentDepth + 1));
            }
            else
            {
                _logger.LogWarning("Failed to create profile for {Name}: {Error}",
                    relativePerson.FullName, errorMessage ?? "Unknown error");

                _results.Add(new SyncResult
                {
                    GedcomId = relativeGedId,
                    PersonName = relativePerson.FullName,
                    Action = SyncAction.Error,
                    RelationType = relationType.ToString(),
                    ErrorMessage = errorMessage ?? "Failed to create profile"
                });
            }
        }
    }

    private async Task<string?> FindMatchInGeniFamilyAsync(
        PersonRecord gedcomPerson,
        RelationType relationType,
        Gender expectedGender,
        GeniImmediateFamily? geniFamily)
    {
        if (geniFamily?.Nodes == null)
            return null;

        // Convert Geni nodes to PersonRecords for matching
        var candidates = new List<(string GeniId, PersonRecord Person)>();

        foreach (var (nodeId, node) in geniFamily.Nodes)
        {
            // Skip if already mapped
            if (_geniToGedcomMap.ContainsKey(nodeId))
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
        }

        if (candidates.Count == 0)
            return null;

        // Find best match
        var matches = _matcher.FindMatches(
            gedcomPerson,
            candidates.Select(c => c.Person),
            _options.MatchingOptions.MatchThreshold);

        if (matches.Count == 0)
            return null;

        var bestMatch = matches[0];

        // Return Geni ID if match is good enough
        if (bestMatch.Score >= _options.MatchingOptions.MatchThreshold)
        {
            var matchedCandidate = candidates.First(c => c.Person.Id == bestMatch.Target.Id);
            return matchedCandidate.GeniId;
        }

        return null;
    }

    private PersonRecord ConvertGeniNodeToPerson(string geniId, GeniNode node)
    {
        return new PersonRecord
        {
            Id = geniId,
            Source = PersonSource.Geni,
            FirstName = node.FirstName,
            LastName = node.LastName,
            NormalizedFirstName = NameNormalizer.Normalize(node.FirstName),
            NormalizedLastName = NameNormalizer.Normalize(node.LastName),
            Gender = node.Gender?.ToLowerInvariant() switch
            {
                "male" => Gender.Male,
                "female" => Gender.Female,
                _ => Gender.Unknown
            },
            BirthDate = DateInfo.Parse(node.BirthDate)
        };
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
            return (null, errorMsg);
        }
        catch (TaskCanceledException ex)
        {
            var errorMsg = "Request timeout";
            _logger.LogError(ex, "Timeout creating {RelType} for {Name}",
                relationType, person.FullName);
            return (null, errorMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "Failed to create {RelType} for {Name} - {Error}",
                relationType, person.FullName, errorMsg);
            return (null, errorMsg);
        }
    }

    #region State Persistence

    private async Task SaveStateAsync()
    {
        var state = new SyncState
        {
            GedcomToGeniMap = _gedcomToGeniMap,
            ProcessedIds = _processedGedcomIds.ToList(),
            Results = _results
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_options.StateFilePath!, json);
        
        _logger.LogInformation("State saved: {Count} mappings, {Processed} processed",
            _gedcomToGeniMap.Count, _processedGedcomIds.Count);
    }

    private async Task LoadStateAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_options.StateFilePath!);
            var state = JsonSerializer.Deserialize<SyncState>(json);

            if (state != null)
            {
                foreach (var (gedId, geniId) in state.GedcomToGeniMap)
                {
                    _gedcomToGeniMap[gedId] = geniId;
                    _geniToGedcomMap[geniId] = gedId;
                }

                foreach (var id in state.ProcessedIds)
                {
                    _processedGedcomIds.Add(id);
                }

                _results.AddRange(state.Results);

                _logger.LogInformation("State loaded: {Count} mappings, {Processed} processed",
                    _gedcomToGeniMap.Count, _processedGedcomIds.Count);
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
            GedcomToGeniMap = new Dictionary<string, string>(_gedcomToGeniMap)
        };

        return report;
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Synchronization options
/// Immutable record for thread-safety
/// </summary>
[ExcludeFromCodeCoverage]
public record SyncOptions
{
    public string? StateFilePath { get; init; }
    public int? MaxDepth { get; init; }
    public MatchingOptions MatchingOptions { get; init; } = new();

    /// <summary>
    /// Enable photo synchronization from GEDCOM to Geni
    /// </summary>
    public bool SyncPhotos { get; init; } = true;
}

public enum RelationType
{
    Parent,
    Child,
    Partner,
    Sibling
}

[ExcludeFromCodeCoverage]
public class SyncState
{
    public Dictionary<string, string> GedcomToGeniMap { get; set; } = new();
    public List<string> ProcessedIds { get; set; } = new();
    public List<SyncResult> Results { get; set; } = new();
}

[ExcludeFromCodeCoverage]
public class SyncReport
{
    public int TotalProcessed { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<SyncResult> Results { get; set; } = new();
    public Dictionary<string, string> GedcomToGeniMap { get; set; } = new();

    public void PrintSummary(ILogger logger)
    {
        logger.LogInformation("=== Sync Report ===");
        logger.LogInformation("Total processed: {Total}", TotalProcessed);
        logger.LogInformation("Matched: {Count} ({Percent:P0})", Matched, (double)Matched / TotalProcessed);
        logger.LogInformation("Created: {Count} ({Percent:P0})", Created, (double)Created / TotalProcessed);
        logger.LogInformation("Skipped: {Count} ({Percent:P0})", Skipped, (double)Skipped / TotalProcessed);
        logger.LogInformation("Errors: {Count} ({Percent:P0})", Errors, (double)Errors / TotalProcessed);
    }

    public void PrintDetails(ILogger logger)
    {
        logger.LogInformation("=== Detailed Results ===");
        
        foreach (var result in Results)
        {
            var status = result.Action switch
            {
                SyncAction.Matched => $"MATCHED (score: {result.MatchScore}%)",
                SyncAction.Created => $"CREATED as {result.RelationType}",
                SyncAction.Skipped => $"SKIPPED: {result.ErrorMessage}",
                SyncAction.Error => $"ERROR: {result.ErrorMessage}",
                _ => "UNKNOWN"
            };

            var geniLink = !string.IsNullOrEmpty(result.GeniId) 
                ? $" → https://www.geni.com/people/{result.GeniId}" 
                : "";

            logger.LogInformation("{GedId}: {Name} - {Status}{Link}",
                result.GedcomId, result.PersonName, status, geniLink);
        }
    }

    public async Task SaveToFileAsync(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}

#endregion
