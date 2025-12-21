using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.ApiClient.Services;

/// <summary>
/// Geni Photo API Client
/// Implements photo-related operations for Geni.com
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniPhotoClient : GeniApiClientBase, IGeniPhotoClient
{
    public GeniPhotoClient(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        bool dryRun,
        ILogger<GeniPhotoClient> logger)
        : base(httpClientFactory, accessToken, dryRun, logger)
    {
    }

    #region Photo Operations

    public async Task<List<GeniPhoto>> GetPhotosAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/photos";
        Logger.LogDebug("GET {Url}", url);

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
            Logger.LogError(ex, "Failed to get photos for profile {ProfileId}", profileId);
            return new List<GeniPhoto>();
        }
    }

    public async Task<GeniPhoto?> AddPhotoAsync(string profileId, string filePath, string? caption = null)
    {
        if (!File.Exists(filePath))
        {
            Logger.LogError(null!, "Photo file not found: {Path}", filePath);
            return null;
        }

        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would upload photo {Path} to profile {ProfileId}",
                filePath, profileId);
            return CreateDryRunPhoto(filePath);
        }

        var url = $"{BaseUrl}/profile-{profileId}/add-photo";
        var fileBytes = await File.ReadAllBytesAsync(filePath);

        return await UploadPhotoInternalAsync(
            url,
            fileBytes,
            Path.GetFileName(filePath),
            profileId,
            caption,
            "Uploaded photo",
            "Failed to upload photo to profile {ProfileId}");
    }

    public async Task<GeniPhoto?> AddPhotoFromBytesAsync(
        string profileId,
        byte[] imageData,
        string fileName,
        string? caption = null)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would upload photo {FileName} ({Size} bytes) to profile {ProfileId}",
                fileName, imageData.Length, profileId);
            return CreateDryRunPhoto(fileName);
        }

        var url = $"{BaseUrl}/profile-{profileId}/add-photo";

        return await UploadPhotoInternalAsync(
            url,
            imageData,
            fileName,
            profileId,
            caption,
            "Uploaded photo",
            "Failed to upload photo to profile {ProfileId}");
    }

    public Task<GeniPhoto?> AddPhotoFromCacheAsync(string profileId, string localPath, string? caption = null)
    {
        return AddPhotoAsync(profileId, localPath, caption);
    }

    public async Task<GeniPhoto?> SetMugshotAsync(string profileId, string filePath)
    {
        if (!File.Exists(filePath))
        {
            Logger.LogError(null!, "Photo file not found: {Path}", filePath);
            return null;
        }

        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would set mugshot {Path} for profile {ProfileId}", filePath, profileId);
            return CreateDryRunPhoto(filePath);
        }

        var url = $"{BaseUrl}/profile-{profileId}/add-mugshot";
        var fileBytes = await File.ReadAllBytesAsync(filePath);

        return await UploadPhotoInternalAsync(
            url,
            fileBytes,
            Path.GetFileName(filePath),
            profileId,
            null,
            "Set mugshot",
            "Failed to set mugshot for profile {ProfileId}");
    }

    public async Task<GeniPhoto?> SetMugshotFromBytesAsync(string profileId, byte[] imageData, string fileName)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would set mugshot {FileName} ({Size} bytes) for profile {ProfileId}",
                fileName, imageData.Length, profileId);
            return CreateDryRunPhoto(fileName);
        }

        var url = $"{BaseUrl}/profile-{profileId}/add-mugshot";

        return await UploadPhotoInternalAsync(
            url,
            imageData,
            fileName,
            profileId,
            null,
            "Set mugshot",
            "Failed to set mugshot for profile {ProfileId}");
    }

    public async Task<bool> SetExistingPhotoAsMugshotAsync(string profileId, string photoId)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would set photo {PhotoId} as mugshot for profile {ProfileId}", photoId, profileId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/update";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("set_mugshot_for", profileId)
        });

        try
        {
            var response = await client.PostAsync(url, formContent);
            response.EnsureSuccessStatusCode();

            Logger.LogInformation("Set photo {PhotoId} as mugshot for profile {ProfileId}", photoId, profileId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to set photo {PhotoId} as mugshot", photoId);
            return false;
        }
    }

    public async Task<GeniPhoto?> UpdatePhotoAsync(string photoId, GeniPhotoUpdate update)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would update photo {PhotoId}: Title={Title}", photoId, update.Title);
            return new GeniPhoto { Id = photoId, Title = update.Title };
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/update";
        Logger.LogDebug("POST {Url}", url);

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
            Logger.LogError(ex, "Failed to update photo {PhotoId}", photoId);
            return null;
        }
    }

    public async Task<bool> DeletePhotoAsync(string photoId)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would delete photo {PhotoId}", photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/delete";
        Logger.LogDebug("POST {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await client.PostAsync(url, null);
            response.EnsureSuccessStatusCode();

            Logger.LogInformation("Deleted photo {PhotoId}", photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to delete photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<bool> TagPhotoAsync(string photoId, string profileId, PhotoTagPosition? position = null)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would tag profile {ProfileId} in photo {PhotoId}", profileId, photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/tag";
        Logger.LogDebug("POST {Url}", url);

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

            Logger.LogInformation("Tagged profile {ProfileId} in photo {PhotoId}", profileId, photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to tag photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<bool> UntagPhotoAsync(string photoId, string profileId)
    {
        if (DryRun)
        {
            Logger.LogInformation("[DRY-RUN] Would untag profile {ProfileId} from photo {PhotoId}", profileId, photoId);
            return true;
        }

        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/untag";
        Logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("profile", profileId)
        });

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            Logger.LogInformation("Untagged profile {ProfileId} from photo {PhotoId}", profileId, photoId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to untag photo {PhotoId}", photoId);
            return false;
        }
    }

    public async Task<List<GeniPhotoTag>> GetPhotoTagsAsync(string photoId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/photo-{photoId}/tags";
        Logger.LogDebug("GET {Url}", url);

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
            Logger.LogError(ex, "Failed to get tags for photo {PhotoId}", photoId);
            return new List<GeniPhotoTag>();
        }
    }

    #endregion

    #region Photo Helper Methods

    private async Task<GeniPhoto?> UploadPhotoInternalAsync(
        string url,
        byte[] imageData,
        string fileName,
        string profileId,
        string? caption,
        string successAction,
        string failureMessage)
    {
        await ThrottleAsync();

        Logger.LogDebug("POST {Url}", url);

        try
        {
            using var client = CreateClient();

            // Convert image data to base64 as required by Geni API
            var base64Image = Convert.ToBase64String(imageData);

            // Use ExecuteWithRetryAsync to handle 429 errors properly
            var response = await ExecuteWithRetryAsync(async () =>
            {
                // Create form content with base64-encoded file
                var formData = new Dictionary<string, string>
                {
                    ["file"] = base64Image
                };

                if (!string.IsNullOrEmpty(caption))
                {
                    formData["title"] = caption;
                }

                var content = new FormUrlEncodedContent(formData);
                var result = await client.PostAsync(url, content);

                // Dispose content after use
                content.Dispose();

                return result;
            });

            response.EnsureSuccessStatusCode();
            var photo = await response.Content.ReadFromJsonAsync<GeniPhoto>();

            Logger.LogInformation("{Action} {PhotoId} for profile {ProfileId}", successAction, photo?.Id, profileId);
            return photo;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, failureMessage, profileId);
            return null;
        }
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
