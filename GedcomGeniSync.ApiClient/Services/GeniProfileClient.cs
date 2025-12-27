using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.ApiClient.Services;

/// <summary>
/// Geni Profile API Client
/// Implements profile-related operations for Geni.com
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniProfileClient : GeniApiClientBase, IGeniProfileClient
{
    // Cache for profile data to avoid redundant API calls
    private readonly Dictionary<string, GeniProfile> _profileCache = new();
    private readonly Dictionary<string, GeniUnion> _unionCache = new();
    private readonly object _cacheLock = new();

    public GeniProfileClient(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        bool dryRun,
        ILogger<GeniProfileClient> logger)
        : base(httpClientFactory, accessToken, dryRun, logger)
    {
    }

    #region Cache Management

    /// <summary>
    /// Gets a cached profile if available
    /// </summary>
    public GeniProfile? GetCachedProfile(string profileId)
    {
        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(profileId);
        lock (_cacheLock)
        {
            if (_profileCache.TryGetValue(cleanId, out var profile))
                return profile;
            if (_profileCache.TryGetValue($"profile-{cleanId}", out profile))
                return profile;
        }
        return null;
    }

    /// <summary>
    /// Gets a cached union if available
    /// </summary>
    public GeniUnion? GetCachedUnion(string unionId)
    {
        var cleanId = unionId.Replace("union-", "");
        lock (_cacheLock)
        {
            if (_unionCache.TryGetValue(cleanId, out var union))
                return union;
            if (_unionCache.TryGetValue($"union-{cleanId}", out union))
                return union;
        }
        return null;
    }

    /// <summary>
    /// Adds a profile to the cache
    /// </summary>
    private void CacheProfile(GeniProfile profile)
    {
        if (profile?.Id == null) return;
        lock (_cacheLock)
        {
            var numericId = profile.NumericId;
            _profileCache[numericId] = profile;
            _profileCache[profile.Id] = profile;
        }
    }

    /// <summary>
    /// Adds a union to the cache
    /// </summary>
    private void CacheUnion(GeniUnion union)
    {
        if (union?.Id == null) return;
        lock (_cacheLock)
        {
            var numericId = union.NumericId;
            _unionCache[numericId] = union;
            _unionCache[$"union-{numericId}"] = union;
            _unionCache[union.Id] = union;
        }
    }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    public (int ProfileCount, int UnionCount) GetCacheStats()
    {
        lock (_cacheLock)
        {
            // Count unique profiles (each is stored multiple times with different keys)
            var uniqueProfiles = _profileCache.Values.Select(p => p.Id).Distinct().Count();
            var uniqueUnions = _unionCache.Values.Select(u => u.Id).Distinct().Count();
            return (uniqueProfiles, uniqueUnions);
        }
    }

    /// <summary>
    /// Clears the cache
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _profileCache.Clear();
            _unionCache.Clear();
        }
        Logger.LogDebug("Profile and union cache cleared");
    }

    #endregion

    #region Read Operations

    public async Task<GeniProfile?> GetProfileAsync(string profileId)
    {
        await ThrottleAsync();

        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(profileId);
        var url = $"{BaseUrl}/profile-{cleanId}";
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

        var dictionary = new Dictionary<string, GeniProfile>();
        var idsToFetch = new List<string>();

        // Check cache first
        foreach (var id in profileIds)
        {
            var cached = GetCachedProfile(id);
            if (cached != null)
            {
                dictionary[cached.NumericId] = cached;
                dictionary[cached.Id] = cached;
            }
            else
            {
                idsToFetch.Add(id);
            }
        }

        if (idsToFetch.Count > 0)
        {
            Logger.LogDebug("Cache hit for {CacheHits} profiles, fetching {ToFetch} from API",
                profileIds.Count - idsToFetch.Count, idsToFetch.Count);

            await ThrottleAsync();

            // Join profile IDs with commas
            var idsParam = string.Join(",", idsToFetch);
            var url = $"{BaseUrl}/profile?ids={idsParam}";
            Logger.LogDebug("GET {Url} (batch of {Count} profiles)", url, idsToFetch.Count);

            try
            {
                using var client = CreateClient();
                var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
                response.EnsureSuccessStatusCode();

                // Log raw JSON response for debugging
                var jsonContent = await response.Content.ReadAsStringAsync();
                Logger.LogDebug("Batch API returned {Length} characters for {Count} profiles",
                    jsonContent.Length, idsToFetch.Count);

                // The batch API returns {"results": [GeniProfile, ...]}
                var batchResult = System.Text.Json.JsonSerializer.Deserialize<GeniBatchProfileResult>(
                    jsonContent,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Convert list to dictionary keyed by numeric ID (without "profile-" prefix)
                if (batchResult?.Results != null)
                {
                    foreach (var profile in batchResult.Results)
                    {
                        var numericId = profile.NumericId;
                        dictionary[numericId] = profile;
                        // Also add with "profile-" prefix for compatibility
                        dictionary[profile.Id] = profile;

                        // Cache the profile for future use
                        CacheProfile(profile);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Failed to get batch profiles for IDs: {ProfileIds}", string.Join(", ", idsToFetch));
            }
        }
        else
        {
            Logger.LogDebug("All {Count} profiles found in cache, skipping API call", profileIds.Count);
        }

        return dictionary;
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

        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(profileId);
        var url = $"{BaseUrl}/profile-{cleanId}/immediate-family";
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

                // Collect all profile IDs and union IDs separately
                var profileIds = new List<string>();
                var unionIds = new List<string>();
                var nodeIdMapping = new Dictionary<string, string>(); // Maps nodeId -> numericId

                foreach (var (nodeId, node) in result.Nodes)
                {
                    if (nodeId.StartsWith("union-"))
                    {
                        // Collect union IDs for batch fetching
                        var unionNumericId = nodeId.Replace("union-", "");
                        unionIds.Add(unionNumericId);
                        Logger.LogDebug("Found union node {NodeId} for batch fetch", nodeId);
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
                            continue; // Will be enriched after union batch fetch
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

                // Fetch all unions in a single batch request
                if (unionIds.Count > 0)
                {
                    Logger.LogInformation("Fetching {Count} unions in a single batch request", unionIds.Count);
                    var batchUnions = await GetUnionsBatchAsync(unionIds);

                    // Enrich union nodes with full union data
                    foreach (var unionId in unionIds)
                    {
                        var nodeId = $"union-{unionId}";

                        // Try to find the union in batch results
                        GeniUnion? fullUnion = null;
                        if (batchUnions.TryGetValue(unionId, out var union))
                        {
                            fullUnion = union;
                        }
                        else if (batchUnions.TryGetValue(nodeId, out var unionWithPrefix))
                        {
                            fullUnion = unionWithPrefix;
                        }

                        if (fullUnion != null)
                        {
                            // Get the original node structure
                            var originalNode = result.Nodes.TryGetValue(nodeId, out var node) ? node : new GeniNode { Id = nodeId };

                            // Enrich the node with union data
                            var enrichedNode = new GeniNode
                            {
                                Id = originalNode.Id ?? nodeId,
                                Edges = originalNode.Edges,
                                Union = fullUnion  // Store the full union data
                            };

                            Logger.LogDebug("Enriched union node {NodeId}: Status='{Status}', MarriageDate='{MarriageDate}', DivorceDate='{DivorceDate}'",
                                nodeId,
                                fullUnion.Status ?? "(null)",
                                fullUnion.MarriageDate ?? "(null)",
                                fullUnion.DivorceDate ?? "(null)");

                            enrichedNodes[nodeId] = enrichedNode;
                        }
                        else
                        {
                            Logger.LogWarning("Failed to fetch full union data for {NodeId} (numeric: {UnionId}), using basic data", nodeId, unionId);
                            enrichedNodes[nodeId] = result.Nodes.TryGetValue(nodeId, out var node) ? node : new GeniNode { Id = nodeId };
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

        var dictionary = new Dictionary<string, GeniUnion>();
        var idsToFetch = new List<string>();

        // Check cache first
        foreach (var id in unionIds)
        {
            var cached = GetCachedUnion(id);
            if (cached != null)
            {
                var numericId = cached.NumericId;
                dictionary[numericId] = cached;
                dictionary[$"union-{numericId}"] = cached;
                if (cached.Id != null)
                    dictionary[cached.Id] = cached;
            }
            else
            {
                idsToFetch.Add(id);
            }
        }

        if (idsToFetch.Count > 0)
        {
            Logger.LogDebug("Cache hit for {CacheHits} unions, fetching {ToFetch} from API",
                unionIds.Count - idsToFetch.Count, idsToFetch.Count);

            await ThrottleAsync();

            // Join union IDs with commas
            var idsParam = string.Join(",", idsToFetch);
            var url = $"{BaseUrl}/union?ids={idsParam}";
            Logger.LogDebug("GET {Url} (batch of {Count} unions)", url, idsToFetch.Count);

            try
            {
                using var client = CreateClient();
                var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
                response.EnsureSuccessStatusCode();

                // Log raw JSON response for debugging
                var jsonContent = await response.Content.ReadAsStringAsync();
                Logger.LogDebug("Batch API returned {Length} characters for {Count} unions",
                    jsonContent.Length, idsToFetch.Count);

                // The batch API returns {"results": [GeniUnion, ...]}
                var batchResult = System.Text.Json.JsonSerializer.Deserialize<GeniBatchUnionResult>(
                    jsonContent,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Convert list to dictionary keyed by numeric ID (without "union-" prefix)
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

                        // Cache the union for future use
                        CacheUnion(union);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Failed to get batch unions for IDs: {UnionIds}", string.Join(", ", idsToFetch));
            }
        }
        else
        {
            Logger.LogDebug("All {Count} unions found in cache, skipping API call", unionIds.Count);
        }

        return dictionary;
    }

    #endregion

    #region Write Operations

    public async Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child)
    {
        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(parentProfileId);
        return await ExecuteAddProfileOperationAsync(
            $"profile-{cleanId}/add-child",
            child,
            () => Logger.LogInformation(
                "[DRY-RUN] Would create child {FirstName} {LastName} for parent {ParentId}",
                child.FirstName,
                child.LastName,
                cleanId),
            result => Logger.LogInformation("Created child profile {ProfileId}", result?.Profile?.Id));
    }

    public async Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent)
    {
        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(childProfileId);
        return await ExecuteAddProfileOperationAsync(
            $"profile-{cleanId}/add-parent",
            parent,
            () => Logger.LogInformation(
                "[DRY-RUN] Would create parent {FirstName} {LastName} for child {ChildId}",
                parent.FirstName,
                parent.LastName,
                cleanId),
            result => Logger.LogInformation("Created parent profile {ProfileId}", result?.Profile?.Id));
    }

    public async Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner)
    {
        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(profileId);
        return await ExecuteAddProfileOperationAsync(
            $"profile-{cleanId}/add-partner",
            partner,
            () => Logger.LogInformation(
                "[DRY-RUN] Would create partner {FirstName} {LastName} for {ProfileId}",
                partner.FirstName,
                partner.LastName,
                cleanId),
            result => Logger.LogInformation("Created partner profile {ProfileId}", result?.Profile?.Id));
    }

    public async Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child)
    {
        return await ExecuteAddProfileOperationAsync(
            $"union-{unionId}/add-child",
            child,
            () => Logger.LogInformation(
                "[DRY-RUN] Would create child {FirstName} {LastName} in union {UnionId}",
                child.FirstName,
                child.LastName,
                unionId),
            result => Logger.LogInformation(
                "Created child profile {ProfileId} in union {UnionId}",
                result?.Profile?.Id,
                unionId));
    }

    public async Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner)
    {
        return await ExecuteAddProfileOperationAsync(
            $"union-{unionId}/add-partner",
            partner,
            () => Logger.LogInformation(
                "[DRY-RUN] Would add partner {FirstName} {LastName} to union {UnionId}",
                partner.FirstName,
                partner.LastName,
                unionId),
            result => Logger.LogInformation(
                "Added partner profile {ProfileId} to union {UnionId}",
                result?.Profile?.Id,
                unionId));
    }

    public async Task<GeniProfile?> UpdateProfileAsync(string profileId, GeniProfileUpdate update)
    {
        if (DryRun)
        {
            // Build list of fields being updated (only non-null values)
            var updateFields = new List<string>();
            if (!string.IsNullOrEmpty(update.FirstName)) updateFields.Add($"FirstName={update.FirstName}");
            if (!string.IsNullOrEmpty(update.MiddleName)) updateFields.Add($"MiddleName={update.MiddleName}");
            if (!string.IsNullOrEmpty(update.LastName)) updateFields.Add($"LastName={update.LastName}");
            if (!string.IsNullOrEmpty(update.MaidenName)) updateFields.Add($"MaidenName={update.MaidenName}");
            if (!string.IsNullOrEmpty(update.Suffix)) updateFields.Add($"Suffix={update.Suffix}");
            if (!string.IsNullOrEmpty(update.Gender)) updateFields.Add($"Gender={update.Gender}");
            if (!string.IsNullOrEmpty(update.Occupation)) updateFields.Add($"Occupation={update.Occupation}");
            if (update.Birth != null) updateFields.Add("Birth");
            if (update.Death != null) updateFields.Add("Death");
            if (update.Burial != null) updateFields.Add("Burial");

            var fieldsStr = updateFields.Count > 0 ? string.Join(", ", updateFields) : "(no fields to update)";
            Logger.LogInformation("[DRY-RUN] Would update profile {ProfileId} with: {Fields}",
                profileId, fieldsStr);

            // In dry-run mode, return the current profile (simulating no change)
            return await GetProfileAsync(profileId);
        }

        await ThrottleAsync();

        var cleanId = ProfileIdHelper.ExtractProfileIdForUrl(profileId);
        var url = $"{BaseUrl}/profile-{cleanId}/update";
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

    /// <summary>
    /// Creates form-encoded content from profile data.
    /// Works with both GeniProfileCreate and GeniProfileUpdate since they inherit from GeniProfileDataBase.
    /// </summary>
    private static FormUrlEncodedContent CreateFormContent(GeniProfileDataBase profile)
    {
        var values = new Dictionary<string, string>();

        // Name fields
        if (!string.IsNullOrEmpty(profile.FirstName))
            values["first_name"] = profile.FirstName;

        if (!string.IsNullOrEmpty(profile.MiddleName))
            values["middle_name"] = profile.MiddleName;

        if (!string.IsNullOrEmpty(profile.LastName))
            values["last_name"] = profile.LastName;

        if (!string.IsNullOrEmpty(profile.MaidenName))
            values["maiden_name"] = profile.MaidenName;

        if (!string.IsNullOrEmpty(profile.Suffix))
            values["suffix"] = profile.Suffix;

        if (!string.IsNullOrEmpty(profile.Title))
            values["title"] = profile.Title;

        if (!string.IsNullOrEmpty(profile.DisplayName))
            values["display_name"] = profile.DisplayName;

        if (!string.IsNullOrEmpty(profile.Gender))
            values["gender"] = profile.Gender;

        // Multilingual names
        // Format: names[locale][field]=value
        // Example: names[ru][first_name]=Иван, names[en][first_name]=Ivan
        if (profile.Names != null)
        {
            foreach (var (locale, fields) in profile.Names)
            {
                foreach (var (field, value) in fields)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        values[$"names[{locale}][{field}]"] = value;
                    }
                }
            }
        }

        // Event objects
        if (profile.Birth != null)
            AddEventToFormData(values, "birth", profile.Birth);

        if (profile.Death != null)
            AddEventToFormData(values, "death", profile.Death);

        if (profile.Baptism != null)
            AddEventToFormData(values, "baptism", profile.Baptism);

        if (profile.Burial != null)
            AddEventToFormData(values, "burial", profile.Burial);

        // Additional info
        if (!string.IsNullOrEmpty(profile.Occupation))
            values["occupation"] = profile.Occupation;

        if (!string.IsNullOrEmpty(profile.Nicknames))
            values["nicknames"] = profile.Nicknames;

        if (!string.IsNullOrEmpty(profile.AboutMe))
            values["about_me"] = profile.AboutMe;

        if (!string.IsNullOrEmpty(profile.CauseOfDeath))
            values["cause_of_death"] = profile.CauseOfDeath;

        // Living status
        if (profile.IsAlive.HasValue)
            values["is_alive"] = profile.IsAlive.Value.ToString().ToLower();

        return new FormUrlEncodedContent(values);
    }

    /// <summary>
    /// Helper method to add event data to form values
    /// Format: event[date][year]=1950, event[date][month]=3, event[date][day]=15, event[location][place_name]=Moscow
    /// </summary>
    private static void AddEventToFormData(Dictionary<string, string> values, string eventName, GeniEventInput eventData)
    {
        if (eventData.Date != null)
        {
            if (eventData.Date.Year.HasValue)
                values[$"{eventName}[date][year]"] = eventData.Date.Year.Value.ToString();

            if (eventData.Date.Month.HasValue)
                values[$"{eventName}[date][month]"] = eventData.Date.Month.Value.ToString();

            if (eventData.Date.Day.HasValue)
                values[$"{eventName}[date][day]"] = eventData.Date.Day.Value.ToString();
        }

        if (eventData.Location != null && !string.IsNullOrEmpty(eventData.Location.PlaceName))
        {
            values[$"{eventName}[location][place_name]"] = eventData.Location.PlaceName;
        }
    }

    private async Task<GeniProfile?> ExecuteAddProfileOperationAsync(
        string urlPath,
        GeniProfileCreate profile,
        Action logDryRun,
        Action<GeniAddResult?> logSuccess)
    {
        if (DryRun)
        {
            logDryRun();
            return CreateDryRunProfile(profile);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/{urlPath}";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(profile);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        // Log raw response to debug deserialization issues
        var jsonContent = await response.Content.ReadAsStringAsync();
        Logger.LogDebug("=== RAW GENI API RESPONSE for {UrlPath} ===", urlPath);
        Logger.LogDebug("{Json}", jsonContent);
        Logger.LogDebug("=== END RAW RESPONSE ===");

        // Try to deserialize as GeniAddResult first (expected format: {"profile": {...}})
        var result = System.Text.Json.JsonSerializer.Deserialize<GeniAddResult>(jsonContent);

        // If profile is null, try deserializing directly as GeniProfile
        // (Geni API may return the profile directly without wrapper)
        if (result?.Profile == null && !string.IsNullOrWhiteSpace(jsonContent))
        {
            Logger.LogInformation("GeniAddResult.Profile is null. Raw API response: {Json}", jsonContent);
            try
            {
                var directProfile = System.Text.Json.JsonSerializer.Deserialize<GeniProfile>(jsonContent);
                if (directProfile?.Id != null)
                {
                    result = new GeniAddResult { Profile = directProfile };
                    Logger.LogInformation("Successfully deserialized directly to GeniProfile: {Id}", directProfile.Id);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                Logger.LogWarning(ex, "Failed to deserialize response as GeniProfile");
            }
        }

        logSuccess(result);

        return result?.Profile;
    }

    private static GeniProfile CreateDryRunProfile(GeniProfileCreate create)
    {
        // Helper to format date from event
        string? FormatDate(GeniEventInput? evt)
        {
            if (evt?.Date == null) return null;
            var parts = new List<string>();
            if (evt.Date.Year.HasValue) parts.Add(evt.Date.Year.Value.ToString());
            if (evt.Date.Month.HasValue) parts.Add(evt.Date.Month.Value.ToString("D2"));
            if (evt.Date.Day.HasValue) parts.Add(evt.Date.Day.Value.ToString("D2"));
            return parts.Count > 0 ? string.Join("-", parts) : null;
        }

        return new GeniProfile
        {
            Id = $"dry-run-{Guid.NewGuid():N}",
            FirstName = create.FirstName,
            LastName = create.LastName,
            Gender = create.Gender,
            BirthDateString = FormatDate(create.Birth),
            BirthLocationString = create.Birth?.Location?.PlaceName,
            DeathDateString = FormatDate(create.Death),
            DeathLocationString = create.Death?.Location?.PlaceName
        };
    }

    #endregion
}
