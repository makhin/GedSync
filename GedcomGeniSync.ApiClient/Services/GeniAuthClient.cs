using System.Diagnostics;
using System.Text.Json;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.ApiClient.Services;

public class GeniAuthClient : IGeniAuthClient
{
    private readonly string _appKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private readonly Func<string?>? _readLineFunc;

    public GeniAuthClient(string appKey, ILogger? logger = null)
        : this(appKey, new HttpClient { BaseAddress = new Uri("https://www.geni.com") }, logger, null)
    {
    }

    public GeniAuthClient(string appKey, HttpClient httpClient, ILogger? logger = null, Func<string?>? readLineFunc = null)
    {
        _appKey = appKey;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _readLineFunc = readLineFunc ?? Console.ReadLine;

        _httpClient.BaseAddress ??= new Uri("https://www.geni.com");
    }

    public Task<GeniAuthToken?> LoadTokenAsync(string tokenFile)
    {
        return LoadTokenFromFileAsync(tokenFile, _logger);
    }

    public static async Task<GeniAuthToken?> LoadTokenFromFileAsync(string tokenFile, ILogger? logger = null)
    {
        if (!File.Exists(tokenFile))
        {
            logger?.LogDebug("Token file not found at {Path}", tokenFile);
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(tokenFile);
            return JsonSerializer.Deserialize<GeniAuthToken>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load token from {Path}", tokenFile);
            return null;
        }
    }

    public async Task SaveTokenAsync(GeniAuthToken token, string tokenFile)
    {
        var json = JsonSerializer.Serialize(token, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(tokenFile, json);
    }

    public async Task<GeniAuthToken?> LoginInteractiveAsync(CancellationToken cancellationToken)
    {
        // Desktop OAuth flow according to https://www.geni.com/platform/developer/help/oauth_desktop?version=1
        var authUrl = $"https://www.geni.com/platform/oauth/authorize?client_id={Uri.EscapeDataString(_appKey)}&response_type=token&display=desktop";

        _logger?.LogInformation("Opening browser for Geni authentication...");
        _logger?.LogInformation("Please log in and authorize the application.");
        _logger?.LogInformation("After authorization, you will be redirected to a success page.");
        OpenBrowser(authUrl);

        _logger?.LogInformation("\nAfter authorization, copy the URL from your browser address bar and paste it here:");
        _logger?.LogInformation("(The URL should start with https://www.geni.com/oauth/auth_success#... or https://www.geni.com//oauth/auth_success#...)");
        _logger?.LogInformation("\nPaste URL: ");

        string? url = null;
        try
        {
            // Read URL from console with cancellation support
            var readTask = Task.Run(() => _readLineFunc?.Invoke(), cancellationToken);
            url = await readTask;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogError("Authentication was cancelled");
            return null;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            _logger?.LogError("No URL provided");
            return null;
        }

        return ParseTokenFromUrl(url);
    }

    public GeniAuthToken? ParseTokenFromUrl(string url)
    {
        try
        {
            // Check if this is an error URL
            if (url.Contains("/platform/oauth/auth_failed"))
            {
                var errorFragment = url.Split('#').LastOrDefault();
                if (!string.IsNullOrEmpty(errorFragment))
                {
                    var errorParams = ParseQueryString(errorFragment);
                    errorParams.TryGetValue("status", out var status);
                    errorParams.TryGetValue("message", out var message);
                    _logger?.LogError("Authorization failed: {Status} - {Message}", status, message);
                }
                else
                {
                    _logger?.LogError("Authorization failed");
                }
                return null;
            }

            // Parse success URL: https://www.geni.com/oauth/auth_success#access_token=TOKEN&expires_in=SECONDS
            // or: https://www.geni.com//oauth/auth_success#access_token=TOKEN&expires_in=SECONDS
            if (!url.Contains("/oauth/auth_success"))
            {
                _logger?.LogError("Invalid authorization URL. Expected URL containing '/oauth/auth_success'");
                return null;
            }

            var fragment = url.Split('#').LastOrDefault();
            if (string.IsNullOrEmpty(fragment))
            {
                _logger?.LogError("No access token found in URL fragment");
                return null;
            }

            // Decode the fragment if it's URL-encoded
            fragment = Uri.UnescapeDataString(fragment);
            _logger?.LogDebug("Parsing fragment: {Fragment}", fragment);

            var parameters = ParseQueryString(fragment);
            if (!parameters.TryGetValue("access_token", out var accessToken) || string.IsNullOrEmpty(accessToken))
            {
                _logger?.LogError("access_token not found in URL");
                return null;
            }

            var expiresIn = 0;
            if (parameters.TryGetValue("expires_in", out var expiresInStr) && int.TryParse(expiresInStr, out var parsed))
            {
                expiresIn = parsed;
            }

            _logger?.LogDebug("Successfully parsed access token from URL (expires in {ExpiresIn} seconds)", expiresIn);

            return new GeniAuthToken
            {
                AccessToken = accessToken,
                RefreshToken = null, // Desktop OAuth flow doesn't provide refresh tokens
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 0))
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse token from URL");
            return null;
        }
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(queryString))
            return result;

        var pairs = queryString.Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                result[key] = value;
            }
        }
        return result;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Fallback for environments without default browser association
        }
    }
}
