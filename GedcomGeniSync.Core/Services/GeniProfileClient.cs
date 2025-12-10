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
            return await response.Content.ReadFromJsonAsync<GeniImmediateFamily>();
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

    private static GeniProfile CreateDryRunProfile(GeniProfileCreate create)
    {
        return new GeniProfile
        {
            Id = $"dry-run-{Guid.NewGuid():N}",
            FirstName = create.FirstName,
            LastName = create.LastName,
            Gender = create.Gender,
            BirthDate = create.BirthDate,
            BirthPlace = create.BirthPlace,
            DeathDate = create.DeathDate,
            DeathPlace = create.DeathPlace
        };
    }

    #endregion
}
