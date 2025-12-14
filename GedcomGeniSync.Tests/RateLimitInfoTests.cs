using GedcomGeniSync.ApiClient.Services;
using Xunit;

namespace GedcomGeniSync.Tests;

public class RateLimitInfoTests
{
    [Fact]
    public void GetRecommendedDelayMs_NoInfo_ReturnsDefaultDelay()
    {
        var rateLimitInfo = new RateLimitInfo();

        var delay = rateLimitInfo.GetRecommendedDelayMs();

        Assert.Equal(1000, delay); // Default 1 second
    }

    [Fact]
    public void GetRecommendedDelayMs_Exceeded_ReturnsFullWindow()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 0,
            WindowSeconds = 60,
            UpdatedAt = DateTime.UtcNow
        };

        var delay = rateLimitInfo.GetRecommendedDelayMs();

        Assert.Equal(60000, delay); // Full window
    }

    [Fact]
    public void GetRecommendedDelayMs_HalfRemaining_ReturnsProportionalDelay()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 50,
            WindowSeconds = 60,
            UpdatedAt = DateTime.UtcNow
        };

        var delay = rateLimitInfo.GetRecommendedDelayMs();

        // Should spread 50 requests across ~60 seconds = ~1200ms per request
        Assert.InRange(delay, 100, 2000);
    }

    [Fact]
    public void IsNearingLimit_LessThan10Percent_ReturnsTrue()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 5, // 5% remaining
            WindowSeconds = 60
        };

        Assert.True(rateLimitInfo.IsNearingLimit);
    }

    [Fact]
    public void IsNearingLimit_MoreThan10Percent_ReturnsFalse()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 50, // 50% remaining
            WindowSeconds = 60
        };

        Assert.False(rateLimitInfo.IsNearingLimit);
    }

    [Fact]
    public void IsExceeded_ZeroRemaining_ReturnsTrue()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 0,
            WindowSeconds = 60
        };

        Assert.True(rateLimitInfo.IsExceeded);
    }

    [Fact]
    public void IsExceeded_PositiveRemaining_ReturnsFalse()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 10,
            WindowSeconds = 60
        };

        Assert.False(rateLimitInfo.IsExceeded);
    }

    [Fact]
    public void ToString_WithValues_ReturnsFormattedString()
    {
        var rateLimitInfo = new RateLimitInfo
        {
            Limit = 100,
            Remaining = 50,
            WindowSeconds = 60
        };

        var result = rateLimitInfo.ToString();

        Assert.Contains("50", result);
        Assert.Contains("100", result);
        Assert.Contains("60", result);
    }

    [Fact]
    public void ToString_WithoutValues_ReturnsUnknown()
    {
        var rateLimitInfo = new RateLimitInfo();

        var result = rateLimitInfo.ToString();

        Assert.Contains("Unknown", result);
    }
}
