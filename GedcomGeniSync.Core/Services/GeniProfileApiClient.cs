using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Geni API Client - Profile Operations
/// This partial class contains profile-related operations and base functionality.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class GeniApiClient : IGeniApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _accessToken;
    private readonly bool _dryRun;
    private readonly ILogger<GeniApiClient> _logger;

    private const string BaseUrl = "https://www.geni.com/api";
    private const int MaxRetries = 3;

    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly RateLimitInfo _rateLimitInfo = new();

    public GeniApiClient(IHttpClientFactory httpClientFactory, string accessToken, bool dryRun, ILogger<GeniApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _accessToken = accessToken;
        _dryRun = dryRun;
        _logger = logger;
    }

    #region Base Infrastructure

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("GeniApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    /// <summary>
    /// Parse rate limit headers from HTTP response
    /// </summary>
    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-API-Rate-Limit", out var limitValues))
        {
            if (int.TryParse(limitValues.FirstOrDefault(), out var limit))
            {
                _rateLimitInfo.Limit = limit;
            }
        }

        if (response.Headers.TryGetValues("X-API-Rate-Remaining", out var remainingValues))
        {
            if (int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                _rateLimitInfo.Remaining = remaining;
            }
        }

        if (response.Headers.TryGetValues("X-API-Rate-Window", out var windowValues))
        {
            if (int.TryParse(windowValues.FirstOrDefault(), out var window))
            {
                _rateLimitInfo.WindowSeconds = window;
            }
        }

        _rateLimitInfo.UpdatedAt = DateTime.UtcNow;

        if (_rateLimitInfo.IsNearingLimit)
        {
            _logger.LogWarning("Approaching rate limit: {RateLimitInfo}", _rateLimitInfo.ToString());
        }
    }

    /// <summary>
    /// Throttle requests based on rate limit information
    /// </summary>
    private async Task ThrottleAsync()
    {
        var delayMs = _rateLimitInfo.GetRecommendedDelayMs();

        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed.TotalMilliseconds < delayMs)
        {
            var waitTime = delayMs - (int)elapsed.TotalMilliseconds;
            _logger.LogDebug("Throttling request: waiting {WaitTimeMs}ms (Rate limit: {Remaining}/{Limit})",
                waitTime, _rateLimitInfo.Remaining, _rateLimitInfo.Limit);
            await Task.Delay(waitTime);
        }

        _lastRequestTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Execute HTTP request with retry logic for 429 (Too Many Requests)
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<Task<HttpResponseMessage>> httpCall,
        int maxRetries = MaxRetries)
    {
        int retryCount = 0;
        int delayMs = 1000;

        while (true)
        {
            try
            {
                var response = await httpCall();

                // Update rate limit info from response headers
                UpdateRateLimitInfo(response);

                // Check for 429 Too Many Requests
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError("Max retries ({MaxRetries}) exceeded due to rate limiting", maxRetries);
                        response.EnsureSuccessStatusCode(); // Will throw
                    }

                    // Check for Retry-After header
                    int retryAfterSeconds = 0;
                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (int.TryParse(retryAfterValues.FirstOrDefault(), out var seconds))
                        {
                            retryAfterSeconds = seconds;
                        }
                    }

                    // Use Retry-After if available, otherwise exponential backoff
                    var waitTimeMs = retryAfterSeconds > 0
                        ? retryAfterSeconds * 1000
                        : delayMs * (int)Math.Pow(2, retryCount);

                    _logger.LogWarning(
                        "Rate limit exceeded (429). Retry {RetryCount}/{MaxRetries} after {WaitTimeMs}ms",
                        retryCount + 1, maxRetries, waitTimeMs);

                    await Task.Delay(waitTimeMs);
                    retryCount++;
                    delayMs = waitTimeMs;
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (retryCount < maxRetries)
            {
                _logger.LogWarning(ex, "HTTP request failed. Retry {RetryCount}/{MaxRetries}",
                    retryCount + 1, maxRetries);
                await Task.Delay(delayMs);
                retryCount++;
                delayMs *= 2;
            }
        }
    }

    #endregion

    #region Read Operations

    public async Task<GeniProfile?> GetProfileAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
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

        var url = $"{BaseUrl}/profile";
        _logger.LogDebug("GET {Url}", url);

        using var client = CreateClient();
        var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GeniProfile>();
    }

    public async Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId)
    {
        await ThrottleAsync();

        var url = $"{BaseUrl}/profile-{profileId}/immediate-family";
        _logger.LogDebug("GET {Url}", url);

        try
        {
            using var client = CreateClient();
            var response = await ExecuteWithRetryAsync(() => client.GetAsync(url));
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
        var url = $"{BaseUrl}/profile/search?names={query}";

        if (!string.IsNullOrEmpty(birthYear))
        {
            url += $"&birth_year={birthYear}";
        }

        _logger.LogDebug("GET {Url}", url);

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

        var url = $"{BaseUrl}/profile-{parentProfileId}/add-child";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
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

        var url = $"{BaseUrl}/profile-{childProfileId}/add-parent";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(parent);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
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

        var url = $"{BaseUrl}/profile-{profileId}/add-partner";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
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

        var url = $"{BaseUrl}/union-{unionId}/add-child";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(child);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
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

        var url = $"{BaseUrl}/union-{unionId}/add-partner";
        _logger.LogDebug("POST {Url}", url);

        using var client = CreateClient();
        var content = CreateFormContent(partner);
        var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeniAddResult>();
        _logger.LogInformation("Added partner profile {ProfileId} to union {UnionId}",
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
