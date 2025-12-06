using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Geni API Client - Photo Operations
/// This partial class contains photo-related operations.
/// </summary>
public partial class GeniApiClient
{
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

    #region Photo Helper Methods

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
