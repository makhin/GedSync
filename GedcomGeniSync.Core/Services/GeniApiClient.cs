using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

[ExcludeFromCodeCoverage]
public class GeniApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _accessToken;
    private readonly bool _dryRun;
    private readonly ILogger<GeniApiClient> _logger;

    private const string BaseUrl = "https://www.geni.com/api";
    private const int RateLimitDelayMs = 1000; // 1 request per second to be safe

    private DateTime _lastRequestTime = DateTime.MinValue;

    public GeniApiClient(IHttpClientFactory httpClientFactory, string accessToken, bool dryRun, ILogger<GeniApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _accessToken = accessToken;
        _dryRun = dryRun;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("GeniApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    #region Rate Limiting
    
    private async Task ThrottleAsync()
    {
        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed.TotalMilliseconds < RateLimitDelayMs)
        {
            await Task.Delay(RateLimitDelayMs - (int)elapsed.TotalMilliseconds);
        }
        _lastRequestTime = DateTime.UtcNow;
    }
    
    #endregion

    #region Read Operations

    public async Task<GeniProfile?> GetProfileAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}?access_token={_accessToken}";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GeniProfile>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<GeniProfile?> GetCurrentUserProfileAsync()
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile?access_token={_accessToken}";
        _logger.LogDebug("GET {Url}", url);

        using var client = CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GeniProfile>();
    }

    public async Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/immediate-family?access_token={_accessToken}";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GeniImmediateFamily>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get immediate family for {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<List<GeniProfile>> SearchProfilesAsync(string name, string? birthYear = null)
    {
        await ThrottleAsync();

        var query = Uri.EscapeDataString(name);
        var url = $"{BaseUrl}/profile/search?names={query}&access_token={_accessToken}";

        if (!string.IsNullOrEmpty(birthYear))
        {
            url += $"&birth_year={birthYear}";
        }

        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniSearchResult>();
            return result?.Results ?? new List<GeniProfile>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search profiles for {Name}", name);
            return new List<GeniProfile>();
        }
    }

    #endregion

    #region Write Operations

    public async Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would create child {FirstName} {LastName} for parent {ParentId}",
                child.FirstName, child.LastName, parentProfileId);
            return CreateDryRunProfile(child);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{parentProfileId}/add-child?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Created child profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would create parent {FirstName} {LastName} for child {ChildId}",
                parent.FirstName, parent.LastName, childProfileId);
            return CreateDryRunProfile(parent);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{childProfileId}/add-parent?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(parent);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Created parent profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would create partner {FirstName} {LastName} for {ProfileId}",
                partner.FirstName, partner.LastName, profileId);
            return CreateDryRunProfile(partner);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-partner?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Created partner profile {ProfileId}", result?.Profile?.Id);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would create child {FirstName} {LastName} in union {UnionId}",
                child.FirstName, child.LastName, unionId);
            return CreateDryRunProfile(child);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/union-{unionId}/add-child?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Created child profile {ProfileId} in union {UnionId}",
            result?.Profile?.Id, unionId);

        return result?.Profile;
    }

    public async Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would add partner {FirstName} {LastName} to union {UnionId}",
                partner.FirstName, partner.LastName, unionId);
            return CreateDryRunProfile(partner);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/union-{unionId}/add-partner?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Added partner profile {ProfileId} to union {UnionId}",
            result?.Profile?.Id, unionId);

        return result?.Profile;
    }

    #endregion

    #region Helpers

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

#region DTOs

[ExcludeFromCodeCoverage]
public class GeniProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }
    
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }
    
    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
    
    [JsonPropertyName("maiden_name")]
    public string? MaidenName { get; set; }
    
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }
    
    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; set; }
    
    [JsonPropertyName("birth_location")]
    public string? BirthPlace { get; set; }
    
    [JsonPropertyName("death_date")]
    public string? DeathDate { get; set; }
    
    [JsonPropertyName("death_location")]
    public string? DeathPlace { get; set; }
    
    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; set; }
    
    [JsonPropertyName("big_tree")]
    public bool? BigTree { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    // Helper to extract numeric ID from full URL
    public string NumericId => Id.Replace("https://www.geni.com/api/profile-", "")
                                 .Replace("profile-", "");
}

[ExcludeFromCodeCoverage]
public class GeniProfileCreate
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? Gender { get; set; } // "male" or "female"
    public string? BirthDate { get; set; } // Format: "YYYY-MM-DD" or "YYYY"
    public string? BirthPlace { get; set; }
    public string? DeathDate { get; set; }
    public string? DeathPlace { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniImmediateFamily
{
    [JsonPropertyName("focus")]
    public GeniProfile? Focus { get; set; }
    
    [JsonPropertyName("nodes")]
    public Dictionary<string, GeniNode>? Nodes { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniNode
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }
    
    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
    
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }
    
    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; set; }
    
    // Relations
    [JsonPropertyName("edges")]
    public GeniEdges? Edges { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniEdges
{
    [JsonPropertyName("union")]
    public List<string>? Unions { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniUnion
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("partners")]
    public List<string>? Partners { get; set; }
    
    [JsonPropertyName("children")]
    public List<string>? Children { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniSearchResult
{
    [JsonPropertyName("results")]
    public List<GeniProfile>? Results { get; set; }
    
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniAddResult
{
    [JsonPropertyName("profile")]
    public GeniProfile? Profile { get; set; }
    
    [JsonPropertyName("union")]
    public GeniUnion? Union { get; set; }
}

#endregion

