using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Geni Profile API Client
/// Implements profile-related operations for Geni.com
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniProfileClient : GeniApiClientBase, IGeniProfileClient
{
    public GeniProfileClient(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        bool dryRun,
        ILogger<GeniProfileClient> logger)
        : base(httpClientFactory, accessToken, dryRun, logger)
    {
    }

    #region Read Operations

    public async Task<GeniProfile?> GetProfileAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}";
        Logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GeniProfile>();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to get profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<Dictionary<string, GeniProfile>> GetProfilesBatchAsync(List<string> profileIds)
    {
        if (profileIds == null || profileIds.Count == 0)
        {
            return new Dictionary<string, GeniProfile>();
        }

        await ThrottleAsync();

        // Join profile IDs with commas
        var idsParam = string.Join(",", profileIds);
        var url = $"{BaseUrl}/profile?ids={idsParam}";
        Logger.LogDebug("GET {Url} (batch of {Count} profiles)", url, profileIds.Count);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();

            // Log raw JSON response for debugging
            var jsonContent = await response.Content.ReadAsStringAsync();
            Logger.LogDebug("Batch API returned {Length} characters for {Count} profiles",
                jsonContent.Length, profileIds.Count);

            // The batch API returns {"results": [GeniProfile, ...]}
            var batchResult = System.Text.Json.JsonSerializer.Deserialize<GeniBatchProfileResult>(
                jsonContent,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Convert list to dictionary keyed by numeric ID (without "profile-" prefix)
            var dictionary = new Dictionary<string, GeniProfile>();
            if (batchResult?.Results != null)
            {
                foreach (var profile in batchResult.Results)
                {
                    var numericId = profile.NumericId;
                    dictionary[numericId] = profile;
                    // Also add with "profile-" prefix for compatibility
                    dictionary[profile.Id] = profile;
                }
            }

            return dictionary;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to get batch profiles for IDs: {ProfileIds}", string.Join(", ", profileIds));
            return new Dictionary<string, GeniProfile>();
        }
    }

    public async Task<GeniProfile?> GetCurrentUserProfileAsync()
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile";
        Logger.LogDebug("GET {Url}", url);

        using var client = CreateClient();
        var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GeniProfile>();
    }

    public async Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/immediate-family";
        Logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();

            // Log raw JSON response for debugging
            var jsonContent = await response.Content.ReadAsStringAsync();

            // Log the full raw JSON response
            Logger.LogInformation("=== RAW GENI API RESPONSE for immediate-family {ProfileId} ===", profileId);
            Logger.LogInformation("{Json}", jsonContent);
            Logger.LogInformation("=== END RAW RESPONSE ===");

            var result = System.Text.Json.JsonSerializer.Deserialize<GeniImmediateFamily>(jsonContent,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // IMPORTANT: The immediate-family endpoint returns only IDs and basic structure
            // We need to fetch full profile data for each node using batch API for efficiency
            if (result?.Nodes != null)
            {
                Logger.LogInformation("Found {Count} nodes in immediate family. Fetching full profile data using batch API...", result.Nodes.Count);

                var enrichedNodes = new Dictionary<string, GeniNode>();

                // Collect all profile IDs (skip union nodes)
                var profileIds = new List<string>();
                var nodeIdMapping = new Dictionary<string, string>(); // Maps nodeId -> numericId

                foreach (var (nodeId, node) in result.Nodes)
                {
                    // Skip union nodes - they're relationships, not people
                    if (nodeId.StartsWith("union-"))
                    {
                        Logger.LogDebug("Skipping union node {NodeId}", nodeId);
                        enrichedNodes[nodeId] = node;
                        continue;
                    }

                    // Extract numeric ID from node ID (e.g., "profile-34829663293" -> "34829663293")
                    var numericId = nodeId.Replace("profile-", "");
                    profileIds.Add(numericId);
                    nodeIdMapping[nodeId] = numericId;
                }

                // Fetch all profiles in a single batch request
                if (profileIds.Count > 0)
                {
                    Logger.LogInformation("Fetching {Count} profiles in a single batch request", profileIds.Count);
                    var batchProfiles = await GetProfilesBatchAsync(profileIds);

                    // Enrich nodes with full profile data
                    foreach (var (nodeId, node) in result.Nodes)
                    {
                        if (nodeId.StartsWith("union-"))
                        {
                            continue; // Already added above
                        }

                        var numericId = nodeIdMapping[nodeId];

                        // Try to find the profile in batch results
                        // The batch API may return profile IDs with or without "profile-" prefix
                        GeniProfile? fullProfile = null;
                        if (batchProfiles.TryGetValue(numericId, out var profile))
                        {
                            fullProfile = profile;
                        }
                        else if (batchProfiles.TryGetValue($"profile-{numericId}", out var profileWithPrefix))
                        {
                            fullProfile = profileWithPrefix;
                        }

                        if (fullProfile != null)
                        {
                            // Enrich the node with full profile data
                            var enrichedNode = new GeniNode
                            {
                                Id = node.Id ?? nodeId,
                                Name = fullProfile.Name,
                                FirstName = fullProfile.FirstName,
                                MiddleName = fullProfile.MiddleName,
                                LastName = fullProfile.LastName,
                                MaidenName = fullProfile.MaidenName,
                                Suffix = fullProfile.Suffix,
                                Names = fullProfile.Names,
                                Gender = fullProfile.Gender,
                                BirthDate = fullProfile.BirthDate,
                                Edges = node.Edges
                            };

                            Logger.LogDebug("Enriched node {NodeId}: Name='{Name}', FirstName='{FirstName}', MiddleName='{MiddleName}', LastName='{LastName}', MaidenName='{MaidenName}', Suffix='{Suffix}', Gender='{Gender}', BirthDate='{BirthDate}', Names={HasNames}",
                                nodeId,
                                enrichedNode.Name ?? "(null)",
                                enrichedNode.FirstName ?? "(null)",
                                enrichedNode.MiddleName ?? "(null)",
                                enrichedNode.LastName ?? "(null)",
                                enrichedNode.MaidenName ?? "(null)",
                                enrichedNode.Suffix ?? "(null)",
                                enrichedNode.Gender ?? "(null)",
                                enrichedNode.BirthDate ?? "(null)",
                                enrichedNode.Names != null ? $"{enrichedNode.Names.Count} locales" : "none");

                            enrichedNodes[nodeId] = enrichedNode;
                        }
                        else
                        {
                            Logger.LogWarning("Failed to fetch full profile for {NodeId} (numeric: {NumericId}), using basic data", nodeId, numericId);
                            enrichedNodes[nodeId] = node;
                        }
                    }
                }

                result.Nodes = enrichedNodes;
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to get immediate family for {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<List<GeniProfile>> SearchProfilesAsync(string name, string? birthYear = null)
    {
        await ThrottleAsync();

        var query = Uri.EscapeDataString(name);
        var url = $"{BaseUrl}/profile/search?names={query}";

        if (!string.IsNullOrEmpty(birthYear))
        {
            url += $"&birth_year={birthYear}";
        }

        Logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniSearchResult>();
            return result?.Results ?? new List<GeniProfile>();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to search profiles for {Name}", name);
            return new List<GeniProfile>();
        }
    }

    public async Task<Dictionary<string, GeniUnion>> GetUnionsBatchAsync(List<string> unionIds)
    {
        if (unionIds == null || unionIds.Count == 0)
        {
            return new Dictionary<string, GeniUnion>();
        }

        await ThrottleAsync();

        // Join union IDs with commas
        var idsParam = string.Join(",", unionIds);
        var url = $"{BaseUrl}/union?ids={idsParam}";
        Logger.LogDebug("GET {Url} (batch of {Count} unions)", url, unionIds.Count);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();

            // Log raw JSON response for debugging
            var jsonContent = await response.Content.ReadAsStringAsync();
            Logger.LogDebug("Batch API returned {Length} characters for {Count} unions",
                jsonContent.Length, unionIds.Count);

            // The batch API returns {"results": [GeniUnion, ...]}
            var batchResult = System.Text.Json.JsonSerializer.Deserialize<GeniBatchUnionResult>(
                jsonContent,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Convert list to dictionary keyed by numeric ID (without "union-" prefix)
            var dictionary = new Dictionary<string, GeniUnion>();
            if (batchResult?.Results != null)
            {
                foreach (var union in batchResult.Results)
                {
                    if (union.Id == null) continue;

                    var numericId = union.NumericId;
                    dictionary[numericId] = union;
                    // Also add with "union-" prefix for compatibility
                    dictionary[$"union-{numericId}"] = union;
                    // Also add with full URL for compatibility
                    dictionary[union.Id] = union;
                }
            }

            return dictionary;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to get batch unions for IDs: {UnionIds}", string.Join(", ", unionIds));
            return new Dictionary<string, GeniUnion>();
        }
    }

    #endregion

    #region Write Operations

    public async Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would create child {FirstName} {LastName} for parent {ParentId}",
                child.FirstName, child.LastName, parentProfileId);
            return CreateDryRunProfile(child);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{parentProfileId}/add-child";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        Logger.LogInformation("Created child profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would create parent {FirstName} {LastName} for child {ChildId}",
                parent.FirstName, parent.LastName, childProfileId);
            return CreateDryRunProfile(parent);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{childProfileId}/add-parent";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(parent);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        Logger.LogInformation("Created parent profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would create partner {FirstName} {LastName} for {ProfileId}",
                partner.FirstName, partner.LastName, profileId);
            return CreateDryRunProfile(partner);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-partner";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        Logger.LogInformation("Created partner profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would create child {FirstName} {LastName} in union {UnionId}",
                child.FirstName, child.LastName, unionId);
            return CreateDryRunProfile(child);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/union-{unionId}/add-child";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        Logger.LogInformation("Created child profile {ProfileId} in union {UnionId}",
            result?.Profile?.Id, unionId);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would add partner {FirstName} {LastName} to union {UnionId}",
                partner.FirstName, partner.LastName, unionId);
            return CreateDryRunProfile(partner);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/union-{unionId}/add-partner";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        Logger.LogInformation("Added partner profile {ProfileId} to union {UnionId}",
            result?.Profile?.Id, unionId);

        return result?.Profile;
    }

    public async Task<GeniProfile?> UpdateProfileAsync(string profileId, GeniProfileUpdate update)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would update profile {ProfileId} with: FirstName={FirstName}, MiddleName={MiddleName}, LastName={LastName}, MaidenName={MaidenName}, Suffix={Suffix}",
                profileId, update.FirstName, update.MiddleName, update.LastName, update.MaidenName, update.Suffix);

            // In dry-run mode, return the current profile (simulating no change)
            return await GetProfileAsync(profileId);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/update";
        Logger.LogDebug("POST {Url}", url);

        try
        {
            using var client = CreateClient();
            var content = CreateFormContent(update);
            var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GeniProfile>();
            Logger.LogInformation("Updated profile {ProfileId}", profileId);

            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to update profile {ProfileId}", profileId);
            return null;
        }
    }

    #endregion

    #region Helper Methods

    private static FormUrlEncodedContent CreateFormContent(GeniProfileCreate profile)
    {
        var values = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(profile.FirstName))
            values["first_name"] = profile.FirstName;

        if (!string.IsNullOrEmpty(profile.LastName))
            values["last_name"] = profile.LastName;

        if (!string.IsNullOrEmpty(profile.MaidenName))
            values["maiden_name"] = profile.MaidenName;

        if (!string.IsNullOrEmpty(profile.Gender))
            values["gender"] = profile.Gender;

        if (!string.IsNullOrEmpty(profile.BirthDate))
            values["birth_date"] = profile.BirthDate;

        if (!string.IsNullOrEmpty(profile.BirthPlace))
            values["birth_location"] = profile.BirthPlace;

        if (!string.IsNullOrEmpty(profile.DeathDate))
            values["death_date"] = profile.DeathDate;

        if (!string.IsNullOrEmpty(profile.DeathPlace))
            values["death_location"] = profile.DeathPlace;

        return new FormUrlEncodedContent(values);
    }

    private static FormUrlEncodedContent CreateFormContent(GeniProfileUpdate update)
    {
        var values = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(update.FirstName))
            values["first_name"] = update.FirstName;

        if (!string.IsNullOrEmpty(update.MiddleName))
            values["middle_name"] = update.MiddleName;

        if (!string.IsNullOrEmpty(update.LastName))
            values["last_name"] = update.LastName;

        if (!string.IsNullOrEmpty(update.MaidenName))
            values["maiden_name"] = update.MaidenName;

        if (!string.IsNullOrEmpty(update.Suffix))
            values["suffix"] = update.Suffix;

        if (!string.IsNullOrEmpty(update.Gender))
            values["gender"] = update.Gender;

        if (!string.IsNullOrEmpty(update.Occupation))
            values["occupation"] = update.Occupation;

        if (!string.IsNullOrEmpty(update.AboutMe))
            values["about_me"] = update.AboutMe;

        // Note: Birth and Death are complex objects and would need special handling
        // For now, we'll skip them as they require structured data

        return new FormUrlEncodedContent(values);
    }

    private static GeniProfile CreateDryRunProfile(GeniProfileCreate create)
    {
        return new GeniProfile
        {
            Id = $"dry-run-{Guid.NewGuid():N}",
            FirstName = create.FirstName,
            LastName = create.LastName,
            Gender = create.Gender,
            BirthDateString = create.BirthDate,
            BirthLocationString = create.BirthPlace,
            DeathDateString = create.DeathDate,
            DeathLocationString = create.DeathPlace
        };
    }

    #endregion
}
