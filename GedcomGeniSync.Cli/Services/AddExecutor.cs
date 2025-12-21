using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Photo;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Executes profile additions based on wave-compare results
/// </summary>
public class AddExecutor
{
    private readonly IGeniProfileClient _profileClient;
    private readonly IGeniPhotoClient _photoClient;
    private readonly IPhotoDownloadService _photoService;
    private readonly IPhotoCacheService? _photoCacheService;
    private readonly GedcomLoadResult _gedcom;
    private readonly ILogger _logger;
    private readonly ProgressTracker? _progressTracker;
    private readonly string? _inputFile;

    public AddExecutor(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient,
        IPhotoDownloadService photoService,
        IPhotoCacheService? photoCacheService,
        GedcomLoadResult gedcom,
        ILogger logger,
        ProgressTracker? progressTracker = null,
        string? inputFile = null)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
        _photoService = photoService;
        _photoCacheService = photoCacheService;
        _gedcom = gedcom;
        _logger = logger;
        _progressTracker = progressTracker;
        _inputFile = inputFile;
    }

    public async Task<Commands.AddResult> ExecuteAdditionsAsync(
        List<NodeToAdd> nodesToAdd,
        System.Collections.Immutable.ImmutableList<NodeToUpdate> existingNodes,
        bool syncPhotos,
        AddProgress? resumeProgress = null)
    {
        var result = new Commands.AddResult();

        // 1. Build map of existing profiles: SourceId -> GeniProfileId
        var profileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingNodes)
        {
            if (!string.IsNullOrEmpty(existing.GeniProfileId))
            {
                profileMap[existing.SourceId] = existing.GeniProfileId;
                _logger.LogDebug("Existing profile: {SourceId} -> {GeniId}",
                    existing.SourceId, existing.GeniProfileId);
            }
        }

        // Add previously created profiles from resume progress
        if (resumeProgress != null)
        {
            foreach (var (sourceId, geniId) in resumeProgress.CreatedProfiles)
            {
                profileMap[sourceId] = geniId;
            }
            _logger.LogInformation("Resuming: loaded {Count} previously created profiles", resumeProgress.CreatedProfiles.Count);
        }

        _logger.LogInformation("Built map of {Count} existing profiles", profileMap.Count);

        // Track progress
        var processedIds = resumeProgress?.ProcessedSourceIds ?? new HashSet<string>();
        var createdProfiles = resumeProgress?.CreatedProfiles ?? new Dictionary<string, string>();
        var addedCount = resumeProgress?.AddedProfiles ?? 0;
        var failedCount = resumeProgress?.FailedProfiles ?? 0;

        // 2. Sort nodes by depth (process closest to existing nodes first)
        var allNodes = nodesToAdd
            .OrderBy(n => n.DepthFromExisting)
            .ThenBy(n => n.SourceId)
            .ToList();

        // Skip already processed nodes if resuming
        var sortedNodes = resumeProgress != null
            ? allNodes.Where(n => !processedIds.Contains(n.SourceId)).ToList()
            : allNodes;

        if (resumeProgress != null)
        {
            _logger.LogInformation("Resuming from previous progress: {Processed}/{Total} already processed",
                processedIds.Count, allNodes.Count);
        }

        _logger.LogInformation("Processing {Count} nodes to add, sorted by depth", sortedNodes.Count);

        // 3. Process each node
        foreach (var node in sortedNodes)
        {
            result.TotalProcessed++;

            try
            {
                var sourcePerson = _gedcom.Persons.GetValueOrDefault(node.SourceId);
                if (sourcePerson == null)
                {
                    _logger.LogWarning("[{SourceId}] Source person not found in GEDCOM, skipping", node.SourceId);
                    result.Skipped++;
                    continue;
                }

                var personSummary = $"{sourcePerson.FullName} ({sourcePerson.BirthDate?.ToString() ?? "?"})";
                _logger.LogInformation("[Depth {Depth}] Processing {Summary} (Source: {SourceId})",
                    node.DepthFromExisting, personSummary, node.SourceId);

                // Find related profile in Geni
                if (string.IsNullOrEmpty(node.RelatedToNodeId))
                {
                    _logger.LogWarning("  No related node specified, skipping");
                    result.Skipped++;
                    continue;
                }

                if (!profileMap.TryGetValue(node.RelatedToNodeId, out var relatedGeniId))
                {
                    _logger.LogWarning("  Related node {RelatedId} not found in profile map, skipping",
                        node.RelatedToNodeId);
                    result.Skipped++;
                    continue;
                }

                _logger.LogInformation("  Related to {RelatedId} (Geni: {GeniId}) as {RelationType}",
                    node.RelatedToNodeId, relatedGeniId, node.RelationType);

                // Map PersonData to GeniProfileCreate
                var profileCreate = MapToGeniProfileCreate(node.PersonData);

                // Create profile using appropriate API
                GeniProfile? createdProfile = null;
                var cleanGeniId = CleanProfileId(relatedGeniId);

                switch (node.RelationType)
                {
                    case CompareRelationType.Parent:
                        // Current person is parent of related node -> add as parent
                        _logger.LogInformation("  Adding as parent to {GeniId}", cleanGeniId);
                        createdProfile = await _profileClient.AddParentAsync(cleanGeniId, profileCreate);
                        break;

                    case CompareRelationType.Child:
                        // Current person is child of related node -> add as child
                        _logger.LogInformation("  Adding as child to {GeniId}", cleanGeniId);
                        createdProfile = await _profileClient.AddChildAsync(cleanGeniId, profileCreate);
                        break;

                    case CompareRelationType.Spouse:
                        // Current person is spouse of related node -> add as partner
                        _logger.LogInformation("  Adding as partner to {GeniId}", cleanGeniId);
                        createdProfile = await _profileClient.AddPartnerAsync(cleanGeniId, profileCreate);
                        break;

                    default:
                        _logger.LogWarning("  Unsupported relation type: {RelationType}", node.RelationType);
                        result.Errors.Add(new Commands.AddError
                        {
                            SourceId = node.SourceId,
                            RelationType = node.RelationType?.ToString() ?? "Unknown",
                            ErrorMessage = "Unsupported relation type"
                        });
                        result.Failed++;
                        continue;
                }

                if (createdProfile == null)
                {
                    _logger.LogError("  ✗ Failed to create profile");
                    result.Failed++;
                    failedCount++;
                    result.Errors.Add(new Commands.AddError
                    {
                        SourceId = node.SourceId,
                        RelationType = node.RelationType?.ToString() ?? "Unknown",
                        ErrorMessage = "API returned null profile"
                    });
                    continue;
                }

                _logger.LogInformation("  ✓ Profile created: {GeniId}", createdProfile.Id);

                // Add to map for future references
                profileMap[node.SourceId] = createdProfile.Id;
                result.CreatedProfiles[node.SourceId] = createdProfile.Id;
                createdProfiles[node.SourceId] = createdProfile.Id;
                result.Successful++;
                addedCount++;

                // Upload photo if available
                if (syncPhotos && !string.IsNullOrEmpty(node.PersonData.PhotoUrl))
                {
                    await UploadPhotoAsync(
                        node.SourceId,
                        createdProfile.Id,
                        node.PersonData.PhotoUrl,
                        result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ✗ Error processing node {SourceId}", node.SourceId);
                result.Failed++;
                failedCount++;
                result.Errors.Add(new Commands.AddError
                {
                    SourceId = node.SourceId,
                    RelationType = node.RelationType?.ToString() ?? "Unknown",
                    ErrorMessage = ex.Message
                });
            }
            finally
            {
                // Mark this node as processed
                processedIds.Add(node.SourceId);

                // Save progress after each profile
                if (_progressTracker != null && !string.IsNullOrEmpty(_inputFile))
                {
                    var progress = new AddProgress
                    {
                        InputFile = _inputFile,
                        GedcomFile = resumeProgress?.GedcomFile ?? "",
                        ProcessedSourceIds = processedIds,
                        CreatedProfiles = createdProfiles,
                        TotalProfiles = allNodes.Count,
                        AddedProfiles = addedCount,
                        FailedProfiles = failedCount
                    };

                    _progressTracker.SaveAddProgress(_inputFile, progress);
                }
            }
        }

        // Clean up progress file on successful completion
        if (_progressTracker != null && !string.IsNullOrEmpty(_inputFile) && result.Failed == 0)
        {
            _progressTracker.DeleteAddProgress(_inputFile);
            _logger.LogInformation("All profiles processed successfully. Progress file deleted.");
        }

        return result;
    }

    /// <summary>
    /// Upload photo for a newly created profile
    /// </summary>
    private async Task UploadPhotoAsync(
        string sourceId,
        string geniProfileId,
        string photoUrl,
        Commands.AddResult result)
    {
        try
        {
            _logger.LogInformation("  Uploading photo from {Url}", photoUrl);

            var (photoData, fileName) = await TryGetCachedPhotoAsync(photoUrl).ConfigureAwait(false);
            if (photoData == null || photoData.Length == 0)
            {
                if (!_photoService.IsSupportedPhotoUrl(photoUrl))
                {
                    _logger.LogWarning("  Photo: Not a supported photo URL, skipping");
                    result.PhotosFailed++;
                    return;
                }

                var downloadResult = await _photoService.DownloadPhotoAsync(photoUrl);
                if (downloadResult == null || downloadResult.Data == null)
                {
                    _logger.LogWarning("  Photo: Failed to download");
                    result.PhotosFailed++;
                    return;
                }

                photoData = downloadResult.Data;
                fileName = downloadResult.FileName;
            }

            var cleanGeniId = CleanProfileId(geniProfileId);
            var uploadedPhoto = await _photoClient.SetMugshotFromBytesAsync(
                cleanGeniId,
                photoData,
                fileName ?? "photo.jpg");

            if (uploadedPhoto != null)
            {
                result.PhotosUploaded++;
                _logger.LogInformation("  ✓ Photo uploaded");
            }
            else
            {
                result.PhotosFailed++;
                _logger.LogWarning("  Photo: Upload failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "  Photo: Error during upload");
            result.PhotosFailed++;
        }
    }

    private async Task<(byte[]? Data, string? FileName)> TryGetCachedPhotoAsync(string photoUrl)
    {
        if (_photoCacheService == null || string.IsNullOrWhiteSpace(photoUrl))
            return (null, null);

        var data = await _photoCacheService.GetPhotoDataAsync(photoUrl).ConfigureAwait(false);
        if (data == null || data.Length == 0)
            return (null, null);

        return (data, GetFileNameFromUrl(photoUrl));
    }

    private static string? GetFileNameFromUrl(string photoUrl)
    {
        if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri))
            return null;

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    /// <summary>
    /// Maps PersonData to GeniProfileCreate
    /// </summary>
    private GeniProfileCreate MapToGeniProfileCreate(PersonData personData)
    {
        return new GeniProfileCreate
        {
            FirstName = personData.FirstName,
            MiddleName = personData.MiddleName,
            LastName = personData.LastName,
            MaidenName = personData.MaidenName,
            Suffix = personData.Suffix,
            Gender = MapGender(personData.Gender),
            Birth = CreateEventInput(personData.BirthDate, personData.BirthPlace),
            Death = CreateEventInput(personData.DeathDate, personData.DeathPlace),
            Burial = CreateEventInput(personData.BurialDate, personData.BurialPlace),
            Occupation = personData.Occupation,
            Nicknames = personData.Nickname
        };
    }

    /// <summary>
    /// Create GeniEventInput from date and place strings
    /// </summary>
    private static GeniEventInput? CreateEventInput(string? dateStr, string? placeStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr) && string.IsNullOrWhiteSpace(placeStr))
            return null;

        var eventInput = new GeniEventInput();

        if (!string.IsNullOrWhiteSpace(dateStr))
        {
            eventInput.Date = ParseDateString(dateStr);
        }

        if (!string.IsNullOrWhiteSpace(placeStr))
        {
            eventInput.Location = new GeniLocationInput { PlaceName = placeStr };
        }

        return eventInput;
    }

    /// <summary>
    /// Parse date string to GeniDateInput
    /// Supports formats: "14 JUL 1934", "1934", "14 JUL 1934", etc.
    /// </summary>
    private static GeniDateInput? ParseDateString(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Remove qualifiers like "ABT", "AFT", "BEF", "EST"
        dateStr = System.Text.RegularExpressions.Regex.Replace(dateStr, @"^(ABT|AFT|BEF|EST|CAL)\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        var result = new GeniDateInput();
        var parts = dateStr.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            // Try to parse as year (4 digits)
            if (part.Length == 4 && int.TryParse(part, out var year))
            {
                result.Year = year;
            }
            // Try to parse as day (1-2 digits)
            else if (part.Length <= 2 && int.TryParse(part, out var day) && day >= 1 && day <= 31)
            {
                result.Day = day;
            }
            // Try to parse as month name
            else
            {
                var monthNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4, ["MAY"] = 5, ["JUN"] = 6,
                    ["JUL"] = 7, ["AUG"] = 8, ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12,
                    ["JANUARY"] = 1, ["FEBRUARY"] = 2, ["MARCH"] = 3, ["APRIL"] = 4, ["JUNE"] = 6,
                    ["JULY"] = 7, ["AUGUST"] = 8, ["SEPTEMBER"] = 9, ["OCTOBER"] = 10, ["NOVEMBER"] = 11, ["DECEMBER"] = 12
                };

                if (monthNames.TryGetValue(part, out var month))
                {
                    result.Month = month;
                }
            }
        }

        return result.Year.HasValue || result.Month.HasValue || result.Day.HasValue ? result : null;
    }

    /// <summary>
    /// Map gender to Geni API format
    /// </summary>
    private string? MapGender(string? gender)
    {
        if (string.IsNullOrEmpty(gender))
            return null;

        return gender.ToUpperInvariant() switch
        {
            "M" => "male",
            "F" => "female",
            "MALE" => "male",
            "FEMALE" => "female",
            _ => null
        };
    }

    /// <summary>
    /// Cleans profile ID by converting to Geni API format (g{numeric_id})
    /// </summary>
    /// <param name="profileId">Profile ID that may contain prefixes like "geni:", "profile-", or "profile-g"</param>
    /// <returns>Profile ID in format g{numeric_id} for use in API URLs</returns>
    private static string CleanProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return profileId;

        // Extract numeric part
        var id = profileId.Contains(':')
            ? profileId[(profileId.LastIndexOf(':') + 1)..]
            : profileId.Replace("profile-", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Ensure g prefix
        return id.StartsWith('g') ? id : $"g{id}";
    }
}
