using FluentAssertions;
using GedcomGeniSync.Services.NameFix;

namespace GedcomGeniSync.Tests.NameFix;

public class DiacriticsRemoverTests
{
    #region RemoveDiacritics Tests

    [Theory]
    [InlineData("Müller", "Muller")]
    [InlineData("Schröder", "Schroder")]
    [InlineData("Weiß", "Weiss")]  // German eszett
    [InlineData("Größe", "Grosse")]
    public void RemoveDiacritics_German_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Šimkus", "Simkus")]
    [InlineData("Kazlauskaitė", "Kazlauskaite")]
    [InlineData("Čeponis", "Ceponis")]
    [InlineData("Žukauskas", "Zukauskas")]
    public void RemoveDiacritics_Lithuanian_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Põld", "Pold")]
    [InlineData("Mägi", "Magi")]
    [InlineData("Kõiv", "Koiv")]
    [InlineData("Üksik", "Uksik")]
    public void RemoveDiacritics_Estonian_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Kowalski", "Kowalski")]  // No change
    [InlineData("Wójcik", "Wojcik")]
    [InlineData("Żółty", "Zolty")]
    [InlineData("Łukasz", "Lukasz")]
    [InlineData("Dąbrowski", "Dabrowski")]
    public void RemoveDiacritics_Polish_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Bērziņš", "Berzins")]
    [InlineData("Jānis", "Janis")]
    [InlineData("Ķēniņš", "Kenins")]
    public void RemoveDiacritics_Latvian_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Björk", "Bjork")]
    [InlineData("Søren", "Soren")]
    [InlineData("Åberg", "Aberg")]
    [InlineData("Æther", "AEther")]
    public void RemoveDiacritics_Nordic_ShouldSimplify(string input, string expected)
    {
        var result = DiacriticsRemover.RemoveDiacritics(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void RemoveDiacritics_PlainAscii_ShouldReturnUnchanged()
    {
        var result = DiacriticsRemover.RemoveDiacritics("Smith");
        result.Should().Be("Smith");
    }

    [Fact]
    public void RemoveDiacritics_Null_ShouldReturnEmpty()
    {
        var result = DiacriticsRemover.RemoveDiacritics(null);
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void RemoveDiacritics_Empty_ShouldReturnEmpty()
    {
        var result = DiacriticsRemover.RemoveDiacritics("");
        result.Should().Be(string.Empty);
    }

    #endregion

    #region IsBasicLatin Tests

    [Theory]
    [InlineData("Smith", true)]
    [InlineData("O'Connor", true)]  // Apostrophe is OK
    [InlineData("Mary-Jane", true)]  // Hyphen is OK
    [InlineData("John Jr.", true)]  // Period is OK
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsBasicLatin_PlainText_ShouldReturnTrue(string? input, bool expected)
    {
        var result = DiacriticsRemover.IsBasicLatin(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Müller", false)]
    [InlineData("Weiß", false)]
    [InlineData("Šimkus", false)]
    [InlineData("Põld", false)]
    [InlineData("Łukasz", false)]
    [InlineData("Иванов", false)]  // Cyrillic
    public void IsBasicLatin_ExtendedChars_ShouldReturnFalse(string input, bool expected)
    {
        var result = DiacriticsRemover.IsBasicLatin(input);
        result.Should().Be(expected);
    }

    #endregion

    #region HasDiacritics Tests

    [Theory]
    [InlineData("Müller", true)]
    [InlineData("Weiß", true)]
    [InlineData("Smith", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasDiacritics_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = DiacriticsRemover.HasDiacritics(input);
        result.Should().Be(expected);
    }

    #endregion
}
