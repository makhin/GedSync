using FluentAssertions;
using GedcomGeniSync.Utils;

namespace GedcomGeniSync.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Иван-Петров", "ivanpetrov")]
    [InlineData("O'Connor", "oconnor")]
    [InlineData("  Marie ", "marie")]
    public void Normalize_ShouldTransliterateAndRemovePunctuation(string input, string expected)
    {
        var result = NameNormalizer.Normalize(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void Transliterate_ShouldReturnSameForLatin()
    {
        NameNormalizer.Transliterate("Alex").Should().Be("Alex");
    }
}
