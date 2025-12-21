using FluentAssertions;
using GedcomGeniSync.Services.Photo;

namespace GedcomGeniSync.Tests;

public class PhotoSourceDetectorTests
{
    [Theory]
    [InlineData("https://media.myheritage.com/photos/photo.jpg", "myheritage")]
    [InlineData("https://media.geni.com/p14/6f/3c/92/7a/photo.jpg", "geni")]
    [InlineData("https://example.com/photos/photo.jpg", "other")]
    public void DetectSource_ShouldClassifyKnownHosts(string url, string expected)
    {
        var result = PhotoSourceDetector.DetectSource(url);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://media.myheritage.com/photo.jpg")]
    [InlineData("https://subdomain.myheritage.com/photo.jpg")]
    public void IsMyHeritageUrl_ShouldMatchMyHeritageHosts(string url)
    {
        PhotoSourceDetector.IsMyHeritageUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://media.geni.com/p14/6f/3c/92/7a/photo.jpg")]
    [InlineData("https://www.geni.com/photo.jpg")]
    public void IsGeniUrl_ShouldMatchGeniHosts(string url)
    {
        PhotoSourceDetector.IsGeniUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://example.com/photo.jpg")]
    public void IsGeniUrl_ShouldRejectUnknownHosts(string url)
    {
        PhotoSourceDetector.IsGeniUrl(url).Should().BeFalse();
    }
}
