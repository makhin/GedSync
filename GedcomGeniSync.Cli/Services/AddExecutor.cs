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
    private readonly InteractiveConfirmationService _confirmationService;
    private readonly ProgressTracker? _progressTracker;
    private readonly string? _inputFile;

    public AddExecutor(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient,
        IPhotoDownloadService photoService,
        IPhotoCacheService? photoCacheService,
        GedcomLoadResult gedcom,
        ILogger logger,
        InteractiveConfirmationService confirmationService,
        ProgressTracker? progressTracker = null,
        string? inputFile = null)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
        _photoService = photoService;
        _photoCacheService = photoCacheService;
        _gedcom = gedcom;
        _logger = logger;
        _confirmationService = confirmationService;
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

                // Skip profiles without first name AND last name - additional safety check
                if (string.IsNullOrWhiteSpace(node.PersonData.FirstName) &&
                    string.IsNullOrWhiteSpace(node.PersonData.LastName))
                {
                    _logger.LogWarning("  Skipping profile without first name or last name (Source: {SourceId})",
                        node.SourceId);
                    result.Skipped++;
                    continue;
                }

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

                // Interactive confirmation if enabled
                if (_confirmationService.IsEnabled)
                {
                    // Prepare primary relative info
                    var primaryRelative = CreateRelativeInfo(
                        node.RelatedToNodeId,
                        relatedGeniId,
                        null);

                    // Prepare additional relatives info
                    var additionalRelatives = new List<RelativeInfo>();
                    foreach (var additionalRel in node.AdditionalRelations)
                    {
                        if (profileMap.TryGetValue(additionalRel.RelatedToNodeId, out var additionalGeniId))
                        {
                            var relInfo = CreateRelativeInfo(
                                additionalRel.RelatedToNodeId,
                                additionalGeniId,
                                additionalRel.RelationType.ToString());
                            additionalRelatives.Add(relInfo);
                        }
                    }

                    var confirmation = _confirmationService.ConfirmAddProfile(
                        node.SourceId,
                        node.PersonData,
                        node.RelationType?.ToString() ?? "Unknown",
                        primaryRelative,
                        additionalRelatives.Count > 0 ? additionalRelatives : null);

                    if (confirmation == ConfirmationResult.Skipped)
                    {
                        _logger.LogWarning("  User skipped adding profile {SourceId}", node.SourceId);
                        result.Skipped++;
                        continue;
                    }

                    if (confirmation == ConfirmationResult.Aborted)
                    {
                        _logger.LogWarning("⊘ Operation aborted by user");
                        throw new OperationCanceledException("Operation aborted by user");
                    }
                }

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
                        // Current person is child of related node -> check for second parent
                        // Phase 2: Handle two parents scenario
                        var secondParentRelation = node.AdditionalRelations
                            .FirstOrDefault(r => r.RelationType == CompareRelationType.Child);

                        if (secondParentRelation != null &&
                            profileMap.TryGetValue(secondParentRelation.RelatedToNodeId, out var secondParentGeniId))
                        {
                            var cleanSecondParentId = CleanProfileId(secondParentGeniId);
                            _logger.LogInformation("  Found second parent {SecondParentId}, looking for common union", cleanSecondParentId);

                            // Find all common unions between both parents
                            var commonUnions = await FindCommonUnionsAsync(cleanGeniId, cleanSecondParentId);

                            if (commonUnions.Count > 0)
                            {
                                // Select the best union (handles multiple unions scenario)
                                var selectedUnion = SelectBestUnion(commonUnions);

                                if (selectedUnion != null && !string.IsNullOrEmpty(selectedUnion.Id))
                                {
                                    // Extract numeric union ID (remove "union-" prefix if present)
                                    var unionId = selectedUnion.Id.Replace("union-", string.Empty);
                                    _logger.LogInformation("  Adding child to union {UnionId}", unionId);
                                    createdProfile = await _profileClient.AddChildToUnionAsync(unionId, profileCreate);
                                    break;
                                }
                            }
                            else
                            {
                                _logger.LogInformation("  No common union found, will add to first parent only");
                            }
                        }

                        // Fallback: add as child to first parent only
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

        // Phase 3: Validate family relations and identify issues requiring manual correction
        if (result.Successful > 0)
        {
            ValidateFamilyRelations(sortedNodes, profileMap, result);
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
    /// <param name="profileId">Profile ID that may contain prefixes like "geni:", "profile-", "profile-g", or "I" (MyHeritage format)</param>
    /// <returns>Profile ID in format g{numeric_id} for use in API URLs</returns>
    private static string CleanProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return profileId;

        // Extract numeric part
        var id = profileId.Contains(':')
            ? profileId[(profileId.LastIndexOf(':') + 1)..]
            : profileId.Replace("profile-", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Remove leading 'I' if present (MyHeritage/GEDCOM format like I6000000207133980253)
        if (id.StartsWith("I", StringComparison.OrdinalIgnoreCase) && id.Length > 1 && char.IsDigit(id[1]))
        {
            id = id.Substring(1);
        }

        // Ensure g prefix
        return id.StartsWith('g') ? id : $"g{id}";
    }

    /// <summary>
    /// Finds all common unions between two profiles (e.g., both parents of a child)
    /// </summary>
    /// <param name="profile1Id">First profile ID (Geni format)</param>
    /// <param name="profile2Id">Second profile ID (Geni format)</param>
    /// <returns>List of common unions found</returns>
    private async Task<List<GeniUnion>> FindCommonUnionsAsync(string profile1Id, string profile2Id)
    {
        var commonUnions = new List<GeniUnion>();

        try
        {
            _logger.LogDebug("  Looking for common unions between {Profile1} and {Profile2}", profile1Id, profile2Id);

            // Get immediate family for first profile
            var family = await _profileClient.GetImmediateFamilyAsync(profile1Id);
            if (family?.Nodes == null)
            {
                _logger.LogDebug("  No family data found for {ProfileId}", profile1Id);
                return commonUnions;
            }

            // Iterate through nodes looking for union nodes
            foreach (var (nodeId, node) in family.Nodes)
            {
                // Skip non-union nodes
                if (!nodeId.StartsWith("union-") || node.Union == null)
                    continue;

                var union = node.Union;

                // Check if both profiles are partners in this union
                var partners = union.Partners ?? new List<string>();

                // Partner IDs might be in different formats, so we normalize them
                var normalizedPartners = partners.Select(p => NormalizeProfileId(p)).ToList();
                var normalizedProfile1 = NormalizeProfileId(profile1Id);
                var normalizedProfile2 = NormalizeProfileId(profile2Id);

                if (normalizedPartners.Contains(normalizedProfile1) && normalizedPartners.Contains(normalizedProfile2))
                {
                    _logger.LogDebug("  Found common union: {UnionId} (Status: {Status})",
                        union.Id, union.Status ?? "active");
                    commonUnions.Add(union);
                }
            }

            if (commonUnions.Count == 0)
            {
                _logger.LogDebug("  No common unions found between {Profile1} and {Profile2}", profile1Id, profile2Id);
            }

            return commonUnions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "  Error finding common unions between {Profile1} and {Profile2}", profile1Id, profile2Id);
            return commonUnions;
        }
    }

    /// <summary>
    /// Selects the best union from multiple candidates
    /// Prefers active unions (not divorced) over ex-spouse unions
    /// </summary>
    /// <param name="unions">List of candidate unions</param>
    /// <returns>The selected union, or null if list is empty</returns>
    private GeniUnion? SelectBestUnion(List<GeniUnion> unions)
    {
        if (unions.Count == 0)
            return null;

        if (unions.Count == 1)
            return unions[0];

        // Multiple unions found - log warning
        _logger.LogWarning("  ⚠ Found {Count} unions between the same partners. Selecting best match...", unions.Count);

        // Strategy: Prefer active unions over ex-spouse
        var activeUnions = unions.Where(u =>
            string.IsNullOrEmpty(u.Status) ||
            !u.Status.Equals("ex_spouse", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activeUnions.Count > 0)
        {
            var selectedUnion = activeUnions[0];
            _logger.LogInformation("  Selected active union: {UnionId}", selectedUnion.Id);

            if (activeUnions.Count > 1)
            {
                _logger.LogWarning("  ⚠ Multiple active unions found. Using first one: {UnionId}. " +
                    "Consider manual review.", selectedUnion.Id);
            }

            return selectedUnion;
        }

        // All unions are ex-spouse - take the first one
        var fallbackUnion = unions[0];
        _logger.LogWarning("  All unions are ex-spouse. Using first one: {UnionId}. " +
            "Consider manual review.", fallbackUnion.Id);
        return fallbackUnion;
    }

    /// <summary>
    /// Normalizes profile ID to a consistent format for comparison.
    /// Handles various formats:
    /// - Full URL: https://www.geni.com/api/profile-34828568625 → 34828568625
    /// - Prefixed: profile-g34828568625, profile-34828568625 → 34828568625
    /// - Short: g34828568625 → 34828568625
    /// - Numeric: 34828568625 → 34828568625
    /// </summary>
    internal static string NormalizeProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return string.Empty;

        var normalized = profileId;

        // Handle full URL format: https://www.geni.com/api/profile-34828568625
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                normalized = normalized[(lastSlash + 1)..];
            }
        }

        // Remove "profile-g" prefix (must be before "profile-" to avoid partial match)
        if (normalized.StartsWith("profile-g", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[9..]; // Length of "profile-g"
        }
        // Remove "profile-" prefix
        else if (normalized.StartsWith("profile-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[8..]; // Length of "profile-"
        }

        // Remove leading 'g' prefix (not all 'g' characters!)
        if (normalized.StartsWith('g') || normalized.StartsWith('G'))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    /// <summary>
    /// Phase 3: Validates family relations and identifies missing links
    /// Since Geni API doesn't support linking existing profiles, this method
    /// generates a report of issues that require manual correction
    /// </summary>
    public void ValidateFamilyRelations(
        List<NodeToAdd> addedNodes,
        Dictionary<string, string> profileMap,
        Commands.AddResult result)
    {
        _logger.LogInformation("Phase 3: Validating family relations for {Count} created profiles...", result.CreatedProfiles.Count);

        foreach (var node in addedNodes)
        {
            // Skip if profile wasn't created
            if (!result.CreatedProfiles.TryGetValue(node.SourceId, out var geniId))
                continue;

            var sourcePerson = _gedcom.Persons.GetValueOrDefault(node.SourceId);
            if (sourcePerson == null)
                continue;

            // Check for missing parent links
            CheckMissingParentLinks(node, sourcePerson, profileMap, geniId, result);

            // Check for missing spouse links
            CheckMissingSpouseLinks(node, sourcePerson, profileMap, geniId, result);
        }

        if (result.RelationIssues.Count > 0)
        {
            _logger.LogWarning("⚠ Found {Count} family relation issues that require manual correction in Geni",
                result.RelationIssues.Count);

            // Group and log issues by type
            var issuesByType = result.RelationIssues.GroupBy(i => i.Type);
            foreach (var group in issuesByType)
            {
                _logger.LogWarning("  - {Type}: {Count} issues", group.Key, group.Count());
            }
        }
        else
        {
            _logger.LogInformation("✓ No family relation issues found");
        }
    }

    /// <summary>
    /// Checks if a child is missing connection to second parent
    /// </summary>
    private void CheckMissingParentLinks(
        NodeToAdd node,
        PersonRecord sourcePerson,
        Dictionary<string, string> profileMap,
        string geniId,
        Commands.AddResult result)
    {
        // Only relevant if this person was added as a child
        if (node.RelationType != CompareRelationType.Child)
            return;

        // Check if person has both parents in GEDCOM
        var hasFather = !string.IsNullOrWhiteSpace(sourcePerson.FatherId);
        var hasMother = !string.IsNullOrWhiteSpace(sourcePerson.MotherId);

        if (!hasFather && !hasMother)
            return;

        // Check which parents exist in Geni
        var fatherInGeni = hasFather && profileMap.ContainsKey(sourcePerson.FatherId!);
        var motherInGeni = hasMother && profileMap.ContainsKey(sourcePerson.MotherId!);

        // If both parents exist in Geni but we only added to one of them
        if (fatherInGeni && motherInGeni)
        {
            // Check if we found a union (which means both parents were linked)
            var secondParent = node.AdditionalRelations
                .FirstOrDefault(r => r.RelationType == CompareRelationType.Child);

            if (secondParent == null)
            {
                // This shouldn't happen with Phase 2 implementation, but log it if it does
                var missingParentId = node.RelatedToNodeId == sourcePerson.FatherId
                    ? sourcePerson.MotherId!
                    : sourcePerson.FatherId!;

                result.RelationIssues.Add(new Commands.RelationIssue
                {
                    Type = Commands.RelationIssueType.MissingParentLink,
                    SourceId = node.SourceId,
                    GeniId = geniId,
                    RelatedSourceId = missingParentId,
                    RelatedGeniId = profileMap.GetValueOrDefault(missingParentId),
                    Description = $"Child {geniId} is missing link to second parent (GEDCOM: {missingParentId}). " +
                                  "This requires manual correction in Geni."
                });

                _logger.LogWarning("  ⚠ {SourceId}: Missing link to second parent {ParentId}",
                    node.SourceId, missingParentId);
            }
        }
    }

    /// <summary>
    /// Checks if two parents who are spouses in GEDCOM are not linked in Geni
    /// </summary>
    private void CheckMissingSpouseLinks(
        NodeToAdd node,
        PersonRecord sourcePerson,
        Dictionary<string, string> profileMap,
        string geniId,
        Commands.AddResult result)
    {
        // Only check for people who have spouses in GEDCOM
        if (sourcePerson.SpouseIds.Count == 0)
            return;

        foreach (var spouseId in sourcePerson.SpouseIds)
        {
            // Skip if spouse doesn't exist in profile map
            if (!profileMap.TryGetValue(spouseId, out var spouseGeniId))
                continue;

            // Check if this spouse was just created (both profiles are new)
            var spouseWasCreated = result.CreatedProfiles.ContainsKey(spouseId);
            var thisWasCreated = result.CreatedProfiles.ContainsKey(node.SourceId);

            // If both profiles were just created, they might not be linked as spouses
            // This can happen when we add two parents separately
            if (spouseWasCreated && thisWasCreated)
            {
                result.RelationIssues.Add(new Commands.RelationIssue
                {
                    Type = Commands.RelationIssueType.MissingSpouseLink,
                    SourceId = node.SourceId,
                    GeniId = geniId,
                    RelatedSourceId = spouseId,
                    RelatedGeniId = spouseGeniId,
                    Description = $"Profiles {geniId} and {spouseGeniId} are spouses in GEDCOM but may not be linked in Geni. " +
                                  "Verify and link manually if needed."
                });

                _logger.LogWarning("  ⚠ {SourceId}: Possible missing spouse link to {SpouseId}",
                    node.SourceId, spouseId);
            }
        }
    }

    /// <summary>
    /// Creates RelativeInfo for display in interactive confirmation
    /// </summary>
    private RelativeInfo CreateRelativeInfo(string sourceId, string geniId, string? relationType)
    {
        var relativePerson = _gedcom.Persons.GetValueOrDefault(sourceId);
        var name = relativePerson != null
            ? $"{relativePerson.FirstName} {relativePerson.MiddleName} {relativePerson.LastName}".Trim()
            : sourceId;

        // Add birth year if available for better identification
        if (relativePerson?.BirthDate != null)
        {
            name += $" (b. {relativePerson.BirthDate})";
        }

        return new RelativeInfo
        {
            SourceId = sourceId,
            GeniId = geniId,
            Name = name,
            RelationType = relationType
        };
    }
}
