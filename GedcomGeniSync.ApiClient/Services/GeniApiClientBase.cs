using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.ApiClient.Services;

/// <summary>
/// Base class for Geni API clients
/// Provides shared infrastructure for HTTP requests, rate limiting, and retry logic
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class GeniApiClientBase
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly string AccessToken;
    protected readonly bool DryRun;
    protected readonly ILogger Logger;

    protected const string BaseUrl = "https://www.geni.com/api";
    protected const int MaxRetries = 3;

    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly RateLimitInfo _rateLimitInfo = new();

    protected GeniApiClientBase(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        bool dryRun,
        ILogger logger)
    {
        HttpClientFactory = httpClientFactory;
        AccessToken = accessToken;
        DryRun = dryRun;
        Logger = logger;
    }

    #region HTTP Client Infrastructure

    protected HttpClient CreateClient()
    {
        var client = HttpClientFactory.CreateClient("GeniApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return client;
    }

    /// <summary>
    /// Parse rate limit headers from HTTP response
    /// </summary>
    protected void UpdateRateLimitInfo(HttpResponseMessage response)
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
            Logger.LogWarning("Approaching rate limit: {RateLimitInfo}", _rateLimitInfo.ToString());
        }
    }

    /// <summary>
    /// Throttle requests based on rate limit information
    /// </summary>
    protected async Task ThrottleAsync()
    {
        var delayMs = _rateLimitInfo.GetRecommendedDelayMs();

        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed.TotalMilliseconds < delayMs)
        {
            var waitTime = delayMs - (int)elapsed.TotalMilliseconds;
            Logger.LogDebug("Throttling request: waiting {WaitTimeMs}ms (Rate limit: {Remaining}/{Limit})",
                waitTime, _rateLimitInfo.Remaining, _rateLimitInfo.Limit);
            await Task.Delay(waitTime);
        }

        _lastRequestTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Execute HTTP request with retry logic for 429 (Too Many Requests)
    /// </summary>
    protected async Task<HttpResponseMessage> ExecuteWithRetryAsync(
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
                        Logger.LogError("Max retries ({MaxRetries}) exceeded due to rate limiting", maxRetries);
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

                    var retryAfterMs = retryAfterSeconds > 0 ? retryAfterSeconds * 1000 : 0;
                    var rateLimitWaitMs = _rateLimitInfo.IsExceeded ? _rateLimitInfo.GetRecommendedDelayMs() : 0;
                    var backoffMs = delayMs * (int)Math.Pow(2, retryCount);
                    var waitTimeMs = Math.Max(backoffMs, Math.Max(retryAfterMs, rateLimitWaitMs));

                    Logger.LogWarning(
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
                Logger.LogWarning(ex, "HTTP request failed. Retry {RetryCount}/{MaxRetries}",
                    retryCount + 1, maxRetries);
                await Task.Delay(delayMs);
                retryCount++;
                delayMs *= 2;
            }
        }
    }

    #endregion
}
