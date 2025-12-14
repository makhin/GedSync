using GedcomGeniSync.ApiClient.Utils;
using Xunit;

namespace GedcomGeniSync.Tests;

public class GeniIdHelperTests
{
    private const string TestNumericId = "6000000206529622827";
    private const string TestIndiId = "@I6000000206529622827@";
    private const string TestRfnId = "geni:6000000206529622827";
    private const string TestProfileId = "profile-6000000206529622827";

    [Theory]
    [InlineData("@I6000000206529622827@", "6000000206529622827")]
    [InlineData("geni:6000000206529622827", "6000000206529622827")]
    [InlineData("profile-6000000206529622827", "6000000206529622827")]
    [InlineData("6000000206529622827", "6000000206529622827")]
    [InlineData("@I123@", "123")]
    [InlineData("geni:456", "456")]
    [InlineData("profile-789", "789")]
    public void ExtractNumericId_ValidFormats_ReturnsNumericId(string input, string expected)
    {
        // Act
        var result = GeniIdHelper.ExtractNumericId(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("@Iabc@")]
    [InlineData("geni:abc")]
    [InlineData("profile-abc")]
    public void ExtractNumericId_InvalidFormats_ReturnsNull(string? input)
    {
        // Act
        var result = GeniIdHelper.ExtractNumericId(input);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("@I6000000206529622827@", "geni:6000000206529622827", true)]
    [InlineData("@I6000000206529622827@", "profile-6000000206529622827", true)]
    [InlineData("geni:6000000206529622827", "profile-6000000206529622827", true)]
    [InlineData("@I6000000206529622827@", "6000000206529622827", true)]
    [InlineData("@I123@", "@I456@", false)]
    [InlineData("geni:123", "geni:456", false)]
    [InlineData("@I123@", "geni:456", false)]
    public void IsSameGeniProfile_VariousInputs_ReturnsCorrectResult(string id1, string id2, bool expected)
    {
        // Act
        var result = GeniIdHelper.IsSameGeniProfile(id1, id2);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "geni:123")]
    [InlineData("@I123@", null)]
    [InlineData("", "geni:123")]
    [InlineData("@I123@", "")]
    [InlineData("invalid", "geni:123")]
    [InlineData("@I123@", "invalid")]
    public void IsSameGeniProfile_InvalidInputs_ReturnsFalse(string? id1, string? id2)
    {
        // Act
        var result = GeniIdHelper.IsSameGeniProfile(id1, id2);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("6000000206529622827", "@I6000000206529622827@")]
    [InlineData("@I6000000206529622827@", "@I6000000206529622827@")]
    [InlineData("geni:6000000206529622827", "@I6000000206529622827@")]
    [InlineData("profile-6000000206529622827", "@I6000000206529622827@")]
    public void ToGedcomIndiId_ValidInputs_ReturnsCorrectFormat(string input, string expected)
    {
        // Act
        var result = GeniIdHelper.ToGedcomIndiId(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    public void ToGedcomIndiId_InvalidInputs_ThrowsArgumentException(string? input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => GeniIdHelper.ToGedcomIndiId(input!));
    }

    [Theory]
    [InlineData("6000000206529622827", "geni:6000000206529622827")]
    [InlineData("geni:6000000206529622827", "geni:6000000206529622827")]
    [InlineData("@I6000000206529622827@", "geni:6000000206529622827")]
    [InlineData("profile-6000000206529622827", "geni:6000000206529622827")]
    public void ToGeniRfnFormat_ValidInputs_ReturnsCorrectFormat(string input, string expected)
    {
        // Act
        var result = GeniIdHelper.ToGeniRfnFormat(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("6000000206529622827", "profile-6000000206529622827")]
    [InlineData("profile-6000000206529622827", "profile-6000000206529622827")]
    [InlineData("@I6000000206529622827@", "profile-6000000206529622827")]
    [InlineData("geni:6000000206529622827", "profile-6000000206529622827")]
    public void ToGeniProfileFormat_ValidInputs_ReturnsCorrectFormat(string input, string expected)
    {
        // Act
        var result = GeniIdHelper.ToGeniProfileFormat(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RealWorldExample_GeniExportedGedcom_IdsMatch()
    {
        // Arrange
        var gedcomIndiId = "@I6000000206529622827@";
        var rfnValue = "geni:6000000206529622827";

        // Act
        var areMatch = GeniIdHelper.IsSameGeniProfile(gedcomIndiId, rfnValue);

        // Assert
        Assert.True(areMatch);
    }

    [Fact]
    public void RealWorldExample_ExtractAndConvert_WorksCorrectly()
    {
        // Arrange
        var gedcomIndiId = "@I6000000206529622827@";

        // Act
        var numericId = GeniIdHelper.ExtractNumericId(gedcomIndiId);
        var rfnFormat = GeniIdHelper.ToGeniRfnFormat(numericId!);
        var profileFormat = GeniIdHelper.ToGeniProfileFormat(numericId!);

        // Assert
        Assert.Equal("6000000206529622827", numericId);
        Assert.Equal("geni:6000000206529622827", rfnFormat);
        Assert.Equal("profile-6000000206529622827", profileFormat);
    }
}
