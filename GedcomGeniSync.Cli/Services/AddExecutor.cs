using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Executes profile additions based on wave-compare results
/// </summary>
public class AddExecutor
{
    private readonly IGeniProfileClient _profileClient;
    private readonly IGeniPhotoClient _photoClient;
    private readonly IMyHeritagePhotoService _photoService;
    private readonly GedcomLoadResult _gedcom;
    private readonly ILogger _logger;

    public AddExecutor(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient,
        IMyHeritagePhotoService photoService,
        GedcomLoadResult gedcom,
        ILogger logger)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
        _photoService = photoService;
        _gedcom = gedcom;
        _logger = logger;
    }

    public async Task<Commands.AddResult> ExecuteAdditionsAsync(
        List<NodeToAdd> nodesToAdd,
        System.Collections.Immutable.ImmutableList<NodeToUpdate> existingNodes,
        bool syncPhotos)
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

        _logger.LogInformation("Built map of {Count} existing profiles", profileMap.Count);

        // 2. Sort nodes by depth (process closest to existing nodes first)
        var sortedNodes = nodesToAdd
            .OrderBy(n => n.DepthFromExisting)
            .ThenBy(n => n.SourceId)
            .ToList();

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
                var cleanGeniId = relatedGeniId.Replace("profile-", "");

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
                result.Successful++;

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
                result.Errors.Add(new Commands.AddError
                {
                    SourceId = node.SourceId,
                    RelationType = node.RelationType?.ToString() ?? "Unknown",
                    ErrorMessage = ex.Message
                });
            }
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
            if (!_photoService.IsMyHeritageUrl(photoUrl))
            {
                _logger.LogWarning("  Photo: Not a MyHeritage URL, skipping");
                result.PhotosFailed++;
                return;
            }

            _logger.LogInformation("  Uploading photo from {Url}", photoUrl);

            var downloadResult = await _photoService.DownloadPhotoAsync(photoUrl);
            if (downloadResult == null || downloadResult.Data == null)
            {
                _logger.LogWarning("  Photo: Failed to download");
                result.PhotosFailed++;
                return;
            }

            var cleanGeniId = geniProfileId.Replace("profile-", "");
            var uploadedPhoto = await _photoClient.SetMugshotFromBytesAsync(
                cleanGeniId,
                downloadResult.Data,
                downloadResult.FileName);

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
            BirthDate = personData.BirthDate,
            BirthPlace = personData.BirthPlace,
            DeathDate = personData.DeathDate,
            DeathPlace = personData.DeathPlace
        };
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
}
