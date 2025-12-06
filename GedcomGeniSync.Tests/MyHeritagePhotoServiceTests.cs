using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GedcomGeniSync.Tests;

public class MyHeritagePhotoServiceTests
{
    private MyHeritagePhotoService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage>? handlerFactory = null,
        bool dryRun = false)
    {
        var httpHandler = new StubHttpMessageHandler(handlerFactory);
        var httpClient = new HttpClient(httpHandler);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient("MyHeritagePhoto"))
            .Returns(httpClient);

        return new MyHeritagePhotoService(
            factoryMock.Object,
            NullLogger<MyHeritagePhotoService>.Instance,
            dryRun);
    }

    [Theory]
    [InlineData("https://www.myheritage.com/image.jpg")]
    [InlineData("https://familysearch.myheritage.com/photo.png")]
    [InlineData("https://media.myheritage.com/resources/pic.gif")]
    [InlineData("https://subdomain.myheritage.com/album/image.webp")]
    public void IsMyHeritageUrl_ShouldRecognizeKnownHosts(string url)
    {
        var service = CreateService();

        var result = service.IsMyHeritageUrl(url);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://example.com/image.jpg")]
    public void IsMyHeritageUrl_ShouldRejectInvalidOrUnknownHosts(string url)
    {
        var service = CreateService();

        var result = service.IsMyHeritageUrl(url);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadPhotoAsync_ShouldReturnDryRunResult_WhenDryRunEnabled()
    {
        var service = CreateService(dryRun: true);

        var result = await service.DownloadPhotoAsync("https://media.myheritage.com/sample.jpg");

        result.Should().NotBeNull();
        result!.Data.Should().BeEmpty();
        result.FileName.Should().Be("dry-run-photo.jpg");
        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task DownloadPhotoAsync_ShouldReturnResult_WhenResponseSuccessful()
    {
        var expectedData = new byte[] { 1, 2, 3 };
        HttpResponseMessage Handler(HttpRequestMessage _)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedData)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }

        var service = CreateService(Handler);

        var result = await service.DownloadPhotoAsync("https://media.myheritage.com/images/photo.jpg");

        result.Should().NotBeNull();
        result!.FileName.Should().Be("photo.jpg");
        result.ContentType.Should().Be("image/jpeg");
        result.Data.Should().Equal(expectedData);
    }

    [Fact]
    public async Task DownloadPhotoAsync_ShouldReturnNull_WhenResponseIsNotSuccessful()
    {
        HttpResponseMessage Handler(HttpRequestMessage _)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var service = CreateService(Handler);

        var result = await service.DownloadPhotoAsync("https://media.myheritage.com/images/missing.jpg");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DownloadPhotosAsync_ShouldSkipNonMyHeritageUrls()
    {
        var expectedData = new byte[] { 9, 9, 9 };
        HttpResponseMessage Handler(HttpRequestMessage _)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedData)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }

        var service = CreateService(Handler);
        var urls = new[]
        {
            "https://media.myheritage.com/valid.png",
            "https://example.com/not-allowed.jpg"
        };

        var results = await service.DownloadPhotosAsync(urls);

        results.Should().HaveCount(1);
        results[0].Url.Should().Be(urls[0]);
        results[0].ContentType.Should().Be("image/png");
        results[0].Data.Should().Equal(expectedData);
    }

    private class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? handler = null)
        {
            _handler = handler ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
