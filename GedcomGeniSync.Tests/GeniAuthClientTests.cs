using System;
using System.Threading;
using System.Threading.Tasks;
using GedcomGeniSync.ApiClient.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GedcomGeniSync.Tests;

public class GeniAuthClientTests
{
    [Fact]
    public void ParseTokenFromUrl_ValidSuccessUrl_ReturnsToken()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://www.geni.com/platform/oauth/auth_success#access_token=test_token_123&expires_in=3600";
        var token = client.ParseTokenFromUrl(url);

        Assert.NotNull(token);
        Assert.Equal("test_token_123", token!.AccessToken);
        Assert.Null(token.RefreshToken); // Desktop OAuth doesn't provide refresh tokens
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(3610)); // ~1 hour with margin
    }

    [Fact]
    public void ParseTokenFromUrl_UrlWithoutExpiresIn_ReturnsTokenWithZeroExpiry()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://www.geni.com/platform/oauth/auth_success#access_token=test_token_456";
        var token = client.ParseTokenFromUrl(url);

        Assert.NotNull(token);
        Assert.Equal("test_token_456", token!.AccessToken);
    }

    [Fact]
    public void ParseTokenFromUrl_UrlWithDoubleSlash_ReturnsToken()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        // Real-world URL format from Geni with double slash
        var url = "https://www.geni.com//oauth/auth_success#access_token=test_token_789&expires_in=86400";
        var token = client.ParseTokenFromUrl(url);

        Assert.NotNull(token);
        Assert.Equal("test_token_789", token!.AccessToken);
        Assert.Null(token.RefreshToken);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ParseTokenFromUrl_UrlEncodedFragment_ReturnsToken()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        // Real-world URL with URL-encoded fragment (as pasted from browser)
        var url = "https://www.geni.com//oauth/auth_success#access_token%3Dno3gDiAFWECxzm0NfJxYNDQg8rWbIkDCw5za8WGf%26expires_in%3D85533";
        var token = client.ParseTokenFromUrl(url);

        Assert.NotNull(token);
        Assert.Equal("no3gDiAFWECxzm0NfJxYNDQg8rWbIkDCw5za8WGf", token!.AccessToken);
        Assert.Null(token.RefreshToken);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ParseTokenFromUrl_ErrorUrl_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://www.geni.com/platform/oauth/auth_failed#status=unauthorized&message=user+canceled";
        var token = client.ParseTokenFromUrl(url);

        Assert.Null(token);
    }

    [Fact]
    public void ParseTokenFromUrl_InvalidUrl_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://example.com/some/random/url";
        var token = client.ParseTokenFromUrl(url);

        Assert.Null(token);
    }

    [Fact]
    public void ParseTokenFromUrl_NoFragment_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://www.geni.com/platform/oauth/auth_success";
        var token = client.ParseTokenFromUrl(url);

        Assert.Null(token);
    }

    [Fact]
    public void ParseTokenFromUrl_NoAccessToken_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();
        var client = new GeniAuthClient("test_app_key", logger: mockLogger.Object);

        var url = "https://www.geni.com/platform/oauth/auth_success#expires_in=3600";
        var token = client.ParseTokenFromUrl(url);

        Assert.Null(token);
    }

    [Fact]
    public async Task LoginInteractiveAsync_WithValidUrl_ReturnsToken()
    {
        var mockLogger = new Mock<ILogger>();
        var successUrl = "https://www.geni.com/platform/oauth/auth_success#access_token=interactive_token&expires_in=3600";

        // Mock Console.ReadLine to return our test URL
        Func<string?> readLineFunc = () => successUrl;

        var client = new GeniAuthClient("test_app_key", new System.Net.Http.HttpClient(), mockLogger.Object, readLineFunc);

        var token = await client.LoginInteractiveAsync(CancellationToken.None);

        Assert.NotNull(token);
        Assert.Equal("interactive_token", token!.AccessToken);
    }

    [Fact]
    public async Task LoginInteractiveAsync_WithEmptyInput_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();

        // Mock Console.ReadLine to return empty string
        Func<string?> readLineFunc = () => "";

        var client = new GeniAuthClient("test_app_key", new System.Net.Http.HttpClient(), mockLogger.Object, readLineFunc);

        var token = await client.LoginInteractiveAsync(CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task LoginInteractiveAsync_WithErrorUrl_ReturnsNull()
    {
        var mockLogger = new Mock<ILogger>();
        var errorUrl = "https://www.geni.com/platform/oauth/auth_failed#status=unauthorized&message=user+denied";

        Func<string?> readLineFunc = () => errorUrl;

        var client = new GeniAuthClient("test_app_key", new System.Net.Http.HttpClient(), mockLogger.Object, readLineFunc);

        var token = await client.LoginInteractiveAsync(CancellationToken.None);

        Assert.Null(token);
    }
}
