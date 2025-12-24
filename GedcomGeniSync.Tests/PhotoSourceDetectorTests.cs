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

    [Theory]
    [InlineData(
        "https://media.geni.com/p14/91/f8/5f/0c/53444869cecb5ef1/500365_406596c6d6561916if4tt3_a_original.jpg?hash=8bb185ea0f2da804d3f869678571ab0ad5edb9f1953aa99226b54aa9383d3abb.1774421999",
        "https://media.geni.com/p14/91/f8/5f/0c/53444869cecb5ef1/500365_406596c6d6561916if4tt3_a_original.jpg")]
    [InlineData(
        "https://media.geni.com/p14/91/f8/5f/0c/photo.jpg?hash=abc123",
        "https://media.geni.com/p14/91/f8/5f/0c/photo.jpg")]
    [InlineData(
        "https://media.geni.com/p14/91/f8/5f/0c/photo.jpg",
        "https://media.geni.com/p14/91/f8/5f/0c/photo.jpg")]
    public void NormalizeCacheKey_ShouldRemoveHashFromGeniUrls(string url, string expected)
    {
        var result = PhotoSourceDetector.NormalizeCacheKey(url);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://example.com/photo.jpg?hash=abc", "https://example.com/photo.jpg?hash=abc")]
    [InlineData("https://media.myheritage.com/photo.jpg?token=123", "https://media.myheritage.com/photo.jpg?token=123")]
    public void NormalizeCacheKey_ShouldNotModifyNonGeniUrls(string url, string expected)
    {
        var result = PhotoSourceDetector.NormalizeCacheKey(url);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCacheKey_ShouldHandleEmptyInput(string? url)
    {
        var result = PhotoSourceDetector.NormalizeCacheKey(url!);

        result.Should().Be(url);
    }
}
