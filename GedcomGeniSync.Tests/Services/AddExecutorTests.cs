using GedcomGeniSync.Cli.Services;
using Xunit;

namespace GedcomGeniSync.Tests.Services;

/// <summary>
/// Tests for AddExecutor helper methods
/// </summary>
public class AddExecutorTests
{
    #region NormalizeProfileId Tests

    [Theory]
    [InlineData("https://www.geni.com/api/profile-34828568625", "34828568625")]
    [InlineData("https://www.geni.com/api/profile-g34828568625", "34828568625")]
    [InlineData("http://www.geni.com/api/profile-34828568625", "34828568625")]
    public void NormalizeProfileId_FullUrl_ReturnsNumericId(string input, string expected)
    {
        var result = AddExecutor.NormalizeProfileId(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("profile-34828568625", "34828568625")]
    [InlineData("profile-g34828568625", "34828568625")]
    [InlineData("PROFILE-34828568625", "34828568625")]
    [InlineData("PROFILE-G34828568625", "34828568625")]
    public void NormalizeProfileId_ProfilePrefix_ReturnsNumericId(string input, string expected)
    {
        var result = AddExecutor.NormalizeProfileId(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("g34828568625", "34828568625")]
    [InlineData("G34828568625", "34828568625")]
    [InlineData("34828568625", "34828568625")]
    public void NormalizeProfileId_ShortFormat_ReturnsNumericId(string input, string expected)
    {
        var result = AddExecutor.NormalizeProfileId(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeProfileId_EmptyOrNull_ReturnsEmpty(string? input, string expected)
    {
        var result = AddExecutor.NormalizeProfileId(input!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeProfileId_BothPartnersFromApi_ShouldMatch()
    {
        // This is the key scenario that was broken:
        // Partners from API are in URL format, but we compare with g-prefixed IDs

        var partnerFromApi = "https://www.geni.com/api/profile-34828568625";
        var profileIdFromMap = "g34828568625";

        var normalizedPartner = AddExecutor.NormalizeProfileId(partnerFromApi);
        var normalizedProfile = AddExecutor.NormalizeProfileId(profileIdFromMap);

        Assert.Equal(normalizedPartner, normalizedProfile);
        Assert.Equal("34828568625", normalizedPartner);
        Assert.Equal("34828568625", normalizedProfile);
    }

    [Fact]
    public void NormalizeProfileId_MultiplePartnersFromApi_AllShouldNormalize()
    {
        // Real data from unions-response.json
        var partners = new[]
        {
            "https://www.geni.com/api/profile-34828568625",
            "https://www.geni.com/api/profile-34829663288"
        };

        var normalized = partners.Select(p => AddExecutor.NormalizeProfileId(p)).ToList();

        Assert.Equal("34828568625", normalized[0]);
        Assert.Equal("34829663288", normalized[1]);
    }

    #endregion
}
