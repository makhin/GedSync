using Xunit;
using GedcomGeniSync.Utils;

namespace GedcomGeniSync.Tests;

public class GedcomIdNormalizerTests
{
    [Theory]
    [InlineData("I1", "@I1@")]
    [InlineData("@I1@", "@I1@")]
    [InlineData("@I1", "@I1@")]
    [InlineData("I1@", "@I1@")]
    [InlineData(" I1 ", "@I1@")]
    [InlineData("  @I1@  ", "@I1@")]
    [InlineData("I500002", "@I500002@")]
    [InlineData("@F123@", "@F123@")]
    [InlineData("F456", "@F456@")]
    public void Normalize_VariousInputFormats_ReturnsStandardFormat(string input, string expected)
    {
        // Act
        var result = GedcomIdNormalizer.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyOrWhitespace_ReturnsInput(string input)
    {
        // Act
        var result = GedcomIdNormalizer.Normalize(input);

        // Assert
        Assert.Equal(input, result);
    }
}
