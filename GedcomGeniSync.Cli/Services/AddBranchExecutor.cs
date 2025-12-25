using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Executes the add-branch operation: creates a branch of relatives from GEDCOM in Geni.
///
/// Algorithm:
/// 1. Start from start-id, create profile and link to anchor-dest
/// 2. Use BFS to traverse all relatives of start-id (excluding anchor direction)
/// 3. For each person, determine relationship to already-created profiles
/// 4. Create profile using appropriate API (AddChild, AddParent, AddPartner)
/// 5. Handle spouse pairs and children with two parents correctly
/// </summary>
public class AddBranchExecutor
{
    private readonly IGeniProfileClient _profileClient;
    private readonly IGeniPhotoClient _photoClient;
    private readonly IPhotoDownloadService _photoService;
    private readonly GedcomLoadResult _gedcom;
    private readonly ILogger _logger;
    private readonly InteractiveConfirmationService _confirmationService;

    // Maps GEDCOM ID -> Geni Profile ID for created profiles
    private readonly Dictionary<string, string> _createdProfiles = new(StringComparer.OrdinalIgnoreCase);

    // Track visited nodes to avoid cycles
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    public AddBranchExecutor(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient,
        IPhotoDownloadService photoService,
        GedcomLoadResult gedcom,
        ILogger logger,
        InteractiveConfirmationService confirmationService)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
        _photoService = photoService;
        _gedcom = gedcom;
        _logger = logger;
        _confirmationService = confirmationService;
    }

    /// <summary>
    /// Execute the add-branch operation
    /// </summary>
    /// <param name="anchorSourceId">Anchor person ID in GEDCOM</param>
    /// <param name="anchorDestId">Anchor profile ID in Geni</param>
    /// <param name="startId">Starting person ID in GEDCOM to create</param>
    /// <param name="startRelationToAnchor">How start-id relates to anchor (Child, Parent, Spouse)</param>
    /// <param name="syncPhotos">Whether to upload photos</param>
    /// <param name="maxDepth">Maximum depth from start-id to traverse</param>
    public async Task<AddBranchResult> ExecuteAsync(
        string anchorSourceId,
        string anchorDestId,
        string startId,
        RelationType startRelationToAnchor,
        bool syncPhotos,
        int maxDepth)
    {
        var result = new AddBranchResult();

        // Initialize: anchor is already in Geni
        var cleanAnchorDestId = CleanProfileId(anchorDestId);
        _createdProfiles[anchorSourceId] = cleanAnchorDestId;
        _visited.Add(anchorSourceId);

        _logger.LogInformation("Starting branch creation from {StartId}", startId);
        _logger.LogInformation("Anchor mapping: {AnchorSource} -> {AnchorDest}", anchorSourceId, cleanAnchorDestId);

        // Get the anchor person to understand family structure
        var anchorPerson = _gedcom.Persons.GetValueOrDefault(anchorSourceId);
        if (anchorPerson == null)
        {
            _logger.LogError("Anchor person {AnchorId} not found in GEDCOM", anchorSourceId);
            return result;
        }

        // Check if anchor has a spouse that's also in Geni (for proper child linking)
        await CheckAndAddAnchorSpouseAsync(anchorPerson, anchorSourceId, cleanAnchorDestId);

        // Use BFS to process the branch
        var queue = new Queue<(string personId, int depth)>();
        queue.Enqueue((startId, 0));
        _visited.Add(startId);

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();

            if (depth > maxDepth)
            {
                _logger.LogDebug("Skipping {PersonId} - exceeds max depth {MaxDepth}", currentId, maxDepth);
                continue;
            }

            var person = _gedcom.Persons.GetValueOrDefault(currentId);
            if (person == null)
            {
                _logger.LogWarning("Person {PersonId} not found in GEDCOM, skipping", currentId);
                continue;
            }

            result.TotalProcessed++;

            // Find a related profile that already exists in Geni
            var (relatedToId, relationType) = FindRelatedCreatedProfile(person);
            if (relatedToId == null)
            {
                _logger.LogWarning("[{PersonId}] No related profile found in Geni, skipping {Name}",
                    currentId, person.FullName);
                result.Skipped++;
                continue;
            }

            var relatedGeniId = _createdProfiles[relatedToId];
            _logger.LogInformation("[Depth {Depth}] Creating {Name} ({PersonId}) as {RelationType} of {RelatedId}",
                depth, person.FullName, currentId, relationType, relatedToId);

            // Interactive confirmation
            if (_confirmationService.IsEnabled)
            {
                var relatedPerson = _gedcom.Persons.GetValueOrDefault(relatedToId);
                var personData = MapToPersonData(person);
                var relativeInfo = new RelativeInfo
                {
                    SourceId = relatedToId,
                    GeniId = relatedGeniId,
                    Name = relatedPerson?.FullName ?? relatedToId
                };

                var confirmation = _confirmationService.ConfirmAddProfile(
                    currentId,
                    personData,
                    relationType.ToString(),
                    relativeInfo,
                    null);

                if (confirmation == ConfirmationResult.Skipped)
                {
                    _logger.LogWarning("User skipped {PersonId}", currentId);
                    result.Skipped++;
                    continue;
                }

                if (confirmation == ConfirmationResult.Aborted)
                {
                    _logger.LogWarning("Operation aborted by user");
                    break;
                }
            }

            // Create the profile
            try
            {
                var createdProfile = await CreateProfileAsync(person, relatedGeniId, relationType);

                if (createdProfile != null)
                {
                    _createdProfiles[currentId] = createdProfile.Id;
                    result.CreatedProfiles[currentId] = createdProfile.Id;
                    result.Created++;

                    _logger.LogInformation("  ✓ Created profile: {GeniId}", createdProfile.Id);

                    // Upload photo if available
                    if (syncPhotos && person.PhotoUrls.Count > 0)
                    {
                        var photoUploaded = await UploadPhotoAsync(person, createdProfile.Id);
                        if (photoUploaded)
                            result.PhotosUploaded++;
                        else
                            result.PhotosFailed++;
                    }
                }
                else
                {
                    _logger.LogError("  ✗ Failed to create profile for {PersonId}", currentId);
                    result.Failed++;
                    result.Errors.Add(new AddBranchError
                    {
                        SourceId = currentId,
                        ErrorMessage = "API returned null profile"
                    });
                    continue; // Don't enqueue relatives if creation failed
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ✗ Error creating profile for {PersonId}", currentId);
                result.Failed++;
                result.Errors.Add(new AddBranchError
                {
                    SourceId = currentId,
                    ErrorMessage = ex.Message
                });
                continue;
            }

            // Enqueue relatives (excluding anchor direction and already visited)
            EnqueueRelatives(person, depth, queue);
        }

        return result;
    }

    /// <summary>
    /// Check if anchor's spouse exists and should be added to the created profiles map.
    /// This helps with proper child-to-union linking.
    /// </summary>
    private async Task CheckAndAddAnchorSpouseAsync(PersonRecord anchorPerson, string anchorSourceId, string anchorGeniId)
    {
        // Get anchor's Geni profile to find their spouse
        try
        {
            var family = await _profileClient.GetImmediateFamilyAsync(anchorGeniId);
            if (family?.Nodes == null) return;

            foreach (var spouseSourceId in anchorPerson.SpouseIds)
            {
                var spouseInGedcom = _gedcom.Persons.GetValueOrDefault(spouseSourceId);
                if (spouseInGedcom == null) continue;

                // Try to find this spouse in Geni's family
                foreach (var (nodeId, node) in family.Nodes)
                {
                    if (!nodeId.StartsWith("profile-")) continue;
                    if (node.Id == null) continue;

                    // Check if this node might be the spouse
                    // Compare by name
                    if (IsLikelyMatchNode(spouseInGedcom, node))
                    {
                        _createdProfiles[spouseSourceId] = node.Id;
                        _visited.Add(spouseSourceId);
                        _logger.LogInformation("Found anchor's spouse in Geni: {SpouseName} ({SourceId}) -> {GeniId}",
                            spouseInGedcom.FullName, spouseSourceId, node.Id);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check anchor's spouse in Geni");
        }
    }

    /// <summary>
    /// Simple check if a GEDCOM person might match a Geni node (from immediate family response)
    /// </summary>
    private static bool IsLikelyMatchNode(PersonRecord gedcomPerson, GeniNode geniNode)
    {
        // Compare first names (case-insensitive)
        var gedcomFirst = gedcomPerson.FirstName?.Trim().ToLowerInvariant() ?? "";
        var geniFirst = geniNode.FirstName?.Trim().ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(gedcomFirst) || string.IsNullOrEmpty(geniFirst))
            return false;

        // Simple name match
        if (gedcomFirst == geniFirst)
        {
            // If birth date string is available, try to extract year and compare
            var gedcomBirthYear = gedcomPerson.BirthYear;
            if (gedcomBirthYear.HasValue && !string.IsNullOrEmpty(geniNode.BirthDate))
            {
                // BirthDate format might be "YYYY-MM-DD" or similar
                if (int.TryParse(geniNode.BirthDate.Split('-')[0], out var geniBirthYear))
                {
                    return Math.Abs(gedcomBirthYear.Value - geniBirthYear) <= 2;
                }
            }

            return true; // Names match, no conflicting birth years
        }

        return false;
    }

    /// <summary>
    /// Find a related person who has already been created in Geni.
    ///
    /// Priority order is important for proper tree structure:
    /// 1. Partner - if spouse/partner exists, add as partner (creates union)
    /// 2. Children - if child exists, add as parent to child
    /// 3. Parents - if parent exists, add as child to parent
    ///
    /// This order ensures that when adding ancestors (parents, grandparents),
    /// we prefer to add them as partners first (if spouse already created),
    /// which creates proper unions. Otherwise we add as parent to existing child.
    /// </summary>
    private (string? relatedToId, RelationType relationType) FindRelatedCreatedProfile(PersonRecord person)
    {
        // Priority 1: Check if any spouse/partner is created
        // This ensures proper union creation when adding couples
        foreach (var spouseId in person.SpouseIds)
        {
            if (_createdProfiles.ContainsKey(spouseId))
            {
                return (spouseId, RelationType.Partner);
            }
        }

        // Priority 2: Check if any child is created (person is parent)
        // This handles adding parents/grandparents going up the tree
        foreach (var childId in person.ChildrenIds)
        {
            if (_createdProfiles.ContainsKey(childId))
            {
                return (childId, RelationType.Parent);
            }
        }

        // Priority 3: Check if either parent is created
        // This handles adding children going down the tree
        if (!string.IsNullOrEmpty(person.FatherId) && _createdProfiles.ContainsKey(person.FatherId))
        {
            return (person.FatherId, RelationType.Child);
        }

        if (!string.IsNullOrEmpty(person.MotherId) && _createdProfiles.ContainsKey(person.MotherId))
        {
            return (person.MotherId, RelationType.Child);
        }

        return (null, RelationType.Child);
    }

    /// <summary>
    /// Create a profile in Geni with the appropriate relationship
    /// </summary>
    private async Task<GeniProfile?> CreateProfileAsync(
        PersonRecord person,
        string relatedGeniId,
        RelationType relationType)
    {
        var profileCreate = MapToGeniProfileCreate(person);
        var cleanRelatedId = CleanProfileId(relatedGeniId);

        switch (relationType)
        {
            case RelationType.Child:
                // Person is child of related -> add as child to parent
                // Check if both parents are in the map
                var secondParentId = GetSecondParentId(person, cleanRelatedId);
                if (secondParentId != null)
                {
                    // Try to find common union and add to it
                    var unionId = await FindUnionBetweenParentsAsync(cleanRelatedId, secondParentId);
                    if (unionId != null)
                    {
                        _logger.LogInformation("  Adding to union {UnionId} (both parents)", unionId);
                        return await _profileClient.AddChildToUnionAsync(unionId, profileCreate);
                    }
                }
                _logger.LogInformation("  Adding as child to {ParentId}", cleanRelatedId);
                return await _profileClient.AddChildAsync(cleanRelatedId, profileCreate);

            case RelationType.Parent:
                // Person is parent of related -> add as parent to child
                _logger.LogInformation("  Adding as parent to {ChildId}", cleanRelatedId);
                return await _profileClient.AddParentAsync(cleanRelatedId, profileCreate);

            case RelationType.Partner:
                // Person is spouse of related -> add as partner
                _logger.LogInformation("  Adding as partner to {PartnerId}", cleanRelatedId);
                return await _profileClient.AddPartnerAsync(cleanRelatedId, profileCreate);

            case RelationType.Sibling:
                // For sibling, we need to add as child to their parent
                // Find the sibling's parent in created profiles
                var siblingPerson = _gedcom.Persons.Values
                    .FirstOrDefault(p => _createdProfiles.ContainsKey(p.Id) && p.SiblingIds.Contains(person.Id));

                if (siblingPerson != null)
                {
                    // Find their common parent
                    if (!string.IsNullOrEmpty(person.FatherId) && _createdProfiles.TryGetValue(person.FatherId, out var fatherId))
                    {
                        return await _profileClient.AddChildAsync(CleanProfileId(fatherId), profileCreate);
                    }
                    if (!string.IsNullOrEmpty(person.MotherId) && _createdProfiles.TryGetValue(person.MotherId, out var motherId))
                    {
                        return await _profileClient.AddChildAsync(CleanProfileId(motherId), profileCreate);
                    }
                }
                _logger.LogWarning("  Cannot add sibling directly, skipping");
                return null;

            default:
                _logger.LogWarning("  Unknown relation type: {RelationType}", relationType);
                return null;
        }
    }

    /// <summary>
    /// Get the second parent's Geni ID if both parents are in the created profiles map
    /// </summary>
    private string? GetSecondParentId(PersonRecord child, string firstParentGeniId)
    {
        // Find which parent matches firstParentGeniId
        string? secondParentSourceId = null;

        if (!string.IsNullOrEmpty(child.FatherId) && _createdProfiles.TryGetValue(child.FatherId, out var fatherGeniId))
        {
            if (CleanProfileId(fatherGeniId) == CleanProfileId(firstParentGeniId))
            {
                // Father is first parent, check if mother exists
                if (!string.IsNullOrEmpty(child.MotherId) && _createdProfiles.ContainsKey(child.MotherId))
                {
                    secondParentSourceId = child.MotherId;
                }
            }
            else
            {
                secondParentSourceId = child.FatherId;
            }
        }

        if (secondParentSourceId == null && !string.IsNullOrEmpty(child.MotherId) &&
            _createdProfiles.TryGetValue(child.MotherId, out var motherGeniId))
        {
            if (CleanProfileId(motherGeniId) != CleanProfileId(firstParentGeniId))
            {
                secondParentSourceId = child.MotherId;
            }
        }

        if (secondParentSourceId != null && _createdProfiles.TryGetValue(secondParentSourceId, out var secondGeniId))
        {
            return CleanProfileId(secondGeniId);
        }

        return null;
    }

    /// <summary>
    /// Find a union between two parents
    /// </summary>
    private async Task<string?> FindUnionBetweenParentsAsync(string parent1Id, string parent2Id)
    {
        try
        {
            var family = await _profileClient.GetImmediateFamilyAsync(parent1Id);
            if (family?.Nodes == null) return null;

            foreach (var (nodeId, node) in family.Nodes)
            {
                if (!nodeId.StartsWith("union-")) continue;
                if (node.Union == null) continue;

                var partners = node.Union.Partners ?? new List<string>();
                var normalizedPartners = partners.Select(NormalizeProfileId).ToList();
                var normalizedParent1 = NormalizeProfileId(parent1Id);
                var normalizedParent2 = NormalizeProfileId(parent2Id);

                if (normalizedPartners.Contains(normalizedParent1) && normalizedPartners.Contains(normalizedParent2))
                {
                    // Return just the numeric part of the union ID
                    return node.Union.Id?.Replace("union-", "") ?? nodeId.Replace("union-", "");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error finding union between {Parent1} and {Parent2}", parent1Id, parent2Id);
        }

        return null;
    }

    /// <summary>
    /// Enqueue relatives for BFS traversal.
    ///
    /// Order is important for proper relationship creation:
    /// 1. Spouses - so they can be added as partners
    /// 2. Parents (as pairs) - father first, then mother, so mother can be added as partner
    /// 3. Children - after parents to maintain proper tree structure
    /// </summary>
    private void EnqueueRelatives(PersonRecord person, int currentDepth, Queue<(string, int)> queue)
    {
        // 1. Enqueue spouse(s) first - they should be processed soon after this person
        foreach (var spouseId in person.SpouseIds)
        {
            if (_visited.Add(spouseId))
            {
                queue.Enqueue((spouseId, currentDepth + 1));
                _logger.LogDebug("  Enqueued spouse: {SpouseId}", spouseId);
            }
        }

        // 2. Enqueue parents as a pair (if both exist)
        // Enqueue one parent, then immediately their spouse, so they're processed together
        var fatherEnqueued = false;
        var motherEnqueued = false;

        if (!string.IsNullOrEmpty(person.FatherId) && _visited.Add(person.FatherId))
        {
            queue.Enqueue((person.FatherId, currentDepth + 1));
            _logger.LogDebug("  Enqueued father: {FatherId}", person.FatherId);
            fatherEnqueued = true;
        }

        // Enqueue mother right after father so they're adjacent in queue
        if (!string.IsNullOrEmpty(person.MotherId) && _visited.Add(person.MotherId))
        {
            queue.Enqueue((person.MotherId, currentDepth + 1));
            _logger.LogDebug("  Enqueued mother: {MotherId}", person.MotherId);
            motherEnqueued = true;
        }

        // If we have both parents, also enqueue father's spouse (mother) immediately if not done
        // This ensures parent couples stay together in processing order
        if (fatherEnqueued && !motherEnqueued && !string.IsNullOrEmpty(person.FatherId))
        {
            var father = _gedcom.Persons.GetValueOrDefault(person.FatherId);
            if (father != null)
            {
                foreach (var fatherSpouseId in father.SpouseIds)
                {
                    if (_visited.Add(fatherSpouseId))
                    {
                        queue.Enqueue((fatherSpouseId, currentDepth + 1));
                        _logger.LogDebug("  Enqueued father's spouse: {SpouseId}", fatherSpouseId);
                    }
                }
            }
        }

        // 3. Enqueue children last
        foreach (var childId in person.ChildrenIds)
        {
            if (_visited.Add(childId))
            {
                queue.Enqueue((childId, currentDepth + 1));
                _logger.LogDebug("  Enqueued child: {ChildId}", childId);
            }
        }

        // Note: We don't enqueue siblings here - they should be discovered through parents
    }

    /// <summary>
    /// Upload photo for a created profile
    /// </summary>
    private async Task<bool> UploadPhotoAsync(PersonRecord person, string geniProfileId)
    {
        var photoUrl = person.PhotoUrls.FirstOrDefault();
        if (string.IsNullOrEmpty(photoUrl)) return false;

        try
        {
            _logger.LogInformation("  Uploading photo from {Url}", photoUrl);

            if (!_photoService.IsSupportedPhotoUrl(photoUrl))
            {
                _logger.LogWarning("  Photo: Not a supported URL, skipping");
                return false;
            }

            var downloadResult = await _photoService.DownloadPhotoAsync(photoUrl);
            if (downloadResult?.Data == null)
            {
                _logger.LogWarning("  Photo: Failed to download");
                return false;
            }

            var cleanGeniId = CleanProfileId(geniProfileId);
            var uploaded = await _photoClient.SetMugshotFromBytesAsync(
                cleanGeniId,
                downloadResult.Data,
                downloadResult.FileName ?? "photo.jpg");

            if (uploaded != null)
            {
                _logger.LogInformation("  ✓ Photo uploaded");
                return true;
            }

            _logger.LogWarning("  Photo: Upload failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "  Photo: Error during upload");
            return false;
        }
    }

    /// <summary>
    /// Map PersonRecord to GeniProfileCreate
    /// </summary>
    private static GeniProfileCreate MapToGeniProfileCreate(PersonRecord person)
    {
        return new GeniProfileCreate
        {
            FirstName = person.FirstName,
            MiddleName = person.MiddleName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,
            Suffix = person.Suffix,
            Gender = MapGender(person.Gender),
            Birth = CreateEventInput(person.BirthDate, person.BirthPlace),
            Death = CreateEventInput(person.DeathDate, person.DeathPlace),
            Burial = CreateEventInput(person.BurialDate, person.BurialPlace),
            Occupation = person.Occupation,
            Nicknames = person.Nickname
        };
    }

    /// <summary>
    /// Map PersonRecord to PersonData for confirmation display
    /// </summary>
    private static PersonData MapToPersonData(PersonRecord person)
    {
        return new PersonData
        {
            FirstName = person.FirstName,
            MiddleName = person.MiddleName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,
            Suffix = person.Suffix,
            Gender = person.Gender.ToString().Substring(0, 1),
            BirthDate = person.BirthDate?.ToString(),
            BirthPlace = person.BirthPlace,
            DeathDate = person.DeathDate?.ToString(),
            DeathPlace = person.DeathPlace,
            Occupation = person.Occupation,
            Nickname = person.Nickname
        };
    }

    private static GeniEventInput? CreateEventInput(DateInfo? date, string? place)
    {
        if (date == null && string.IsNullOrEmpty(place))
            return null;

        return new GeniEventInput
        {
            Date = date != null ? new GeniDateInput
            {
                Year = date.Year,
                Month = date.Month,
                Day = date.Day
            } : null,
            Location = !string.IsNullOrEmpty(place) ? new GeniLocationInput { PlaceName = place } : null
        };
    }

    private static string? MapGender(Gender gender)
    {
        return gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            _ => null
        };
    }

    // Use ProfileIdHelper.CleanProfileId and ProfileIdHelper.NormalizeProfileId
    private static string CleanProfileId(string profileId) => ProfileIdHelper.CleanProfileId(profileId);
    private static string NormalizeProfileId(string profileId) => ProfileIdHelper.NormalizeProfileId(profileId);
}

/// <summary>
/// Result of add-branch operation
/// </summary>
public class AddBranchResult
{
    public int TotalProcessed { get; set; }
    public int Created { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int PhotosUploaded { get; set; }
    public int PhotosFailed { get; set; }
    public Dictionary<string, string> CreatedProfiles { get; set; } = new();
    public List<AddBranchError> Errors { get; set; } = new();
}

/// <summary>
/// Error during add-branch operation
/// </summary>
public class AddBranchError
{
    public required string SourceId { get; set; }
    public required string ErrorMessage { get; set; }
}
