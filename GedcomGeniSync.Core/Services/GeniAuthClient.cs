using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using GedcomGeniSync.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

public class GeniAuthClient : IGeniAuthClient
{
    private readonly string _appKey;
    private readonly string _appSecret;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;

    public GeniAuthClient(string appKey, string appSecret, ILogger? logger = null)
    {
        _appKey = appKey;
        _appSecret = appSecret;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.geni.com")
        };
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

    public async Task<GeniAuthToken?> LoginInteractiveAsync(int port, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var redirectUri = $"http://localhost:{port}/callback";
        var authUrl =
            $"https://www.geni.com/platform/oauth/authorize?client_id={_appKey}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code";

        _logger?.LogInformation("Opening browser for Geni authentication...");
        OpenBrowser(authUrl);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.EndsWith('/') ? redirectUri : redirectUri + "/");
        listener.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var context = await listener.GetContextAsync().WaitAsync(cts.Token);
            var code = context.Request.QueryString["code"];

            if (string.IsNullOrEmpty(code))
            {
                _logger?.LogError("Authorization code not found in callback");
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "Authorization failed. You can close this tab.");
                return null;
            }

            await RespondAsync(context.Response, HttpStatusCode.OK, "Authentication successful. You can close this tab.");
            return await ExchangeCodeForTokenAsync(code, redirectUri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogError("Authentication timed out or was cancelled");
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<GeniAuthToken?> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var content = new StringContent(
            $"grant_type=authorization_code&client_id={_appKey}&client_secret={_appSecret}&code={code}&redirect_uri={Uri.EscapeDataString(redirectUri)}",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var response = await _httpClient.PostAsync("/platform/oauth/request_token", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Token request failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(payload);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenProperty))
        {
            _logger?.LogError("Token response missing access_token");
            return null;
        }

        var accessToken = accessTokenProperty.GetString() ?? string.Empty;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 0;

        return new GeniAuthToken
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 0))
        };
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

    private static async Task RespondAsync(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }
}
