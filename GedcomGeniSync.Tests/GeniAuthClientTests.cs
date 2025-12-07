using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GedcomGeniSync.Services;
using Xunit;

namespace GedcomGeniSync.Tests;

public class GeniAuthClientTests
{
    [Fact]
    public async Task ExchangeCodeForTokenAsync_UsesUrlEncodedContent()
    {
        var handler = new RecordingHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.geni.com")
        };
        var client = new GeniAuthClient("app key", "secret/with+chars", httpClient);

        var token = await client.ExchangeCodeForTokenAsync(
            "abc+def/==",
            "http://localhost:123/callback path",
            CancellationToken.None);

        Assert.NotNull(token);
        Assert.Equal("access", token!.AccessToken);

        var payload = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("grant_type=authorization_code", payload);
        Assert.Contains("client_id=app+key", payload);
        Assert.Contains("client_secret=secret%2Fwith%2Bchars", payload);
        Assert.Contains("code=abc%2Bdef%2F%3D%3D", payload);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A123%2Fcallback+path", payload);
    }

    private class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"access\",\"expires_in\":60}")
            });
        }
    }
}
