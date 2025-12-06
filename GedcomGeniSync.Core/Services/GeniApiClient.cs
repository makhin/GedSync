using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using System.IO;

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

    #region Photo Operations

    public async Task<List<GeniPhoto>> GetPhotosAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/photos?access_token={_accessToken}";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhotoListResult>();
            return result?.Results ?? new List<GeniPhoto>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get photos for profile {ProfileId}", profileId);
            return new List<GeniPhoto>();
        }
    }

    public async Task<GeniPhoto?> AddPhotoAsync(string profileId, string filePath, string? caption = null)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError(null!, "Photo file not found: {Path}", filePath);
            return null;
        }

        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would upload photo {Path} to profile {ProfileId}",
                filePath, profileId);
            return CreateDryRunPhoto(filePath);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-photo?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = GetContentType(filePath);
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        if (!string.IsNullOrEmpty(caption))
        {
            content.Add(new StringContent(caption), "title");
        }

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhoto>();

            _logger.LogInformation("Uploaded photo {PhotoId} to profile {ProfileId}", result?.Id, profileId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to upload photo to profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<GeniPhoto?> AddPhotoFromBytesAsync(
        string profileId,
        byte[] imageData,
        string fileName,
        string? caption = null)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would upload photo {FileName} ({Size} bytes) to profile {ProfileId}",
                fileName, imageData.Length, profileId);
            return CreateDryRunPhoto(fileName);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-photo?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new MultipartFormDataContent();

        using var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType = GetContentType(fileName);
        content.Add(fileContent, "file", fileName);

        if (!string.IsNullOrEmpty(caption))
        {
            content.Add(new StringContent(caption), "title");
        }

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhoto>();

            _logger.LogInformation("Uploaded photo {PhotoId} to profile {ProfileId}", result?.Id, profileId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to upload photo to profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<GeniPhoto?> SetMugshotAsync(string profileId, string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError(null!, "Photo file not found: {Path}", filePath);
            return null;
        }

        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would set mugshot {Path} for profile {ProfileId}", filePath, profileId);
            return CreateDryRunPhoto(filePath);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-mugshot?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = GetContentType(filePath);
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhoto>();

            _logger.LogInformation("Set mugshot {PhotoId} for profile {ProfileId}", result?.Id, profileId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to set mugshot for profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<GeniPhoto?> SetMugshotFromBytesAsync(string profileId, byte[] imageData, string fileName)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would set mugshot {FileName} ({Size} bytes) for profile {ProfileId}",
                fileName, imageData.Length, profileId);
            return CreateDryRunPhoto(fileName);
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/add-mugshot?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new MultipartFormDataContent();

        using var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType = GetContentType(fileName);
        content.Add(fileContent, "file", fileName);

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhoto>();

            _logger.LogInformation("Set mugshot {PhotoId} for profile {ProfileId}", result?.Id, profileId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to set mugshot for profile {ProfileId}", profileId);
            return null;
        }
    }

    public async Task<bool> SetExistingPhotoAsMugshotAsync(string profileId, string photoId)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would set photo {PhotoId} as mugshot for profile {ProfileId}", photoId, profileId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/update?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("set_mugshot_for", profileId)
        });

        try
        {
            var response = await client.PostAsync(url, formContent);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Set photo {PhotoId} as mugshot for profile {ProfileId}", photoId, profileId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to set photo {PhotoId} as mugshot", photoId);
            return false;
        }
    }

    public async Task<GeniPhoto?> UpdatePhotoAsync(string photoId, GeniPhotoUpdate update)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would update photo {PhotoId}: Title={Title}", photoId, update.Title);
            return new GeniPhoto { Id = photoId, Title = update.Title };
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/update?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        var values = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(update.Title))
            values["title"] = update.Title;

        if (!string.IsNullOrEmpty(update.Date))
            values["date"] = update.Date;

        if (!string.IsNullOrEmpty(update.Location))
            values["location"] = update.Location;

        using var content = new FormUrlEncodedContent(values);

        try
        {
            using var client = CreateClient();
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GeniPhoto>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update photo {PhotoId}", photoId);
            return null;
        }
    }

    public async Task<bool> DeletePhotoAsync(string photoId)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would delete photo {PhotoId}", photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/delete?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.PostAsync(url, null);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Deleted photo {PhotoId}", photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to delete photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<bool> TagPhotoAsync(string photoId, string profileId, PhotoTagPosition? position = null)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would tag profile {ProfileId} in photo {PhotoId}", profileId, photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/tag?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        var values = new Dictionary<string, string>
        {
            ["profile"] = profileId
        };

        if (position != null)
        {
            values["x"] = position.X.ToString("F2", CultureInfo.InvariantCulture);
            values["y"] = position.Y.ToString("F2", CultureInfo.InvariantCulture);
            values["width"] = position.Width.ToString("F2", CultureInfo.InvariantCulture);
            values["height"] = position.Height.ToString("F2", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(values);

        try
        {
            using var client = CreateClient();
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Tagged profile {ProfileId} in photo {PhotoId}", profileId, photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to tag photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<bool> UntagPhotoAsync(string photoId, string profileId)
    {
        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would untag profile {ProfileId} from photo {PhotoId}", profileId, photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/untag?access_token={_accessToken}";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("profile", profileId)
        });

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Untagged profile {ProfileId} from photo {PhotoId}", profileId, photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to untag photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<List<GeniPhotoTag>> GetPhotoTagsAsync(string photoId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/tags?access_token={_accessToken}";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GeniPhotoTagsResult>();
            return result?.Results ?? new List<GeniPhotoTag>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get tags for photo {PhotoId}", photoId);
            return new List<GeniPhotoTag>();
        }
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

    private static MediaTypeHeaderValue GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var mimeType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        return new MediaTypeHeaderValue(mimeType);
    }

    private static GeniPhoto CreateDryRunPhoto(string filePath)
    {
        return new GeniPhoto
        {
            Id = $"dry-run-{Guid.NewGuid():N}",
            Title = Path.GetFileNameWithoutExtension(filePath),
            Url = $"file://{filePath}"
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

public class GeniPhoto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("content_url")]
    public string? ContentUrl { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    public string NumericId => Id
        .Replace("https://www.geni.com/api/photo-", "")
        .Replace("photo-", "");
}

public class GeniPhotoUpdate
{
    public string? Title { get; set; }
    public string? Date { get; set; }
    public string? Location { get; set; }
}

public class GeniPhotoTag
{
    [JsonPropertyName("profile")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("width")]
    public double? Width { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }
}

public class PhotoTagPosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class GeniPhotoListResult
{
    [JsonPropertyName("results")]
    public List<GeniPhoto>? Results { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

public class GeniPhotoTagsResult
{
    [JsonPropertyName("results")]
    public List<GeniPhotoTag>? Results { get; set; }
}

#endregion

