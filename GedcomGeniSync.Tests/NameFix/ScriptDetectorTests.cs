using FluentAssertions;
using GedcomGeniSync.Services.NameFix;

namespace GedcomGeniSync.Tests.NameFix;

public class ScriptDetectorTests
{
    #region Script Detection

    [Theory]
    [InlineData("Hello World", ScriptDetector.TextScript.Latin)]
    [InlineData("Иван Петров", ScriptDetector.TextScript.Cyrillic)]
    [InlineData("Привет Hello", ScriptDetector.TextScript.Mixed)]
    [InlineData("שלום", ScriptDetector.TextScript.Hebrew)]
    [InlineData("123 !@#", ScriptDetector.TextScript.Unknown)]
    [InlineData("", ScriptDetector.TextScript.Unknown)]
    [InlineData(null, ScriptDetector.TextScript.Unknown)]
    public void DetectScript_ShouldDetectCorrectly(string? input, ScriptDetector.TextScript expected)
    {
        var result = ScriptDetector.DetectScript(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Иванов", true)]
    [InlineData("Ivanov", false)]
    [InlineData("Иванов Ivanov", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsCyrillic_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.ContainsCyrillic(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Ivanov", true)]
    [InlineData("Иванов", false)]
    [InlineData("Иванов Ivanov", true)]
    [InlineData("Müller", true)]  // Extended Latin
    public void ContainsLatin_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.ContainsLatin(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Иванов", true)]
    [InlineData("Иванов-Петров", true)]  // Hyphen is not a letter
    [InlineData("Ivanov", false)]
    [InlineData("Иванов123", true)]  // Numbers are not letters
    public void IsPurelyCyrillic_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.IsPurelyCyrillic(input);
        result.Should().Be(expected);
    }

    #endregion

    #region Language Detection

    [Theory]
    [InlineData("Jonaitis", true)]  // Lithuanian masculine ending
    [InlineData("Kazlauskaitė", true)]  // Lithuanian feminine ending with special char
    [InlineData("Šimkus", true)]  // Lithuanian special character
    [InlineData("Smith", false)]
    [InlineData("Иванов", false)]  // Cyrillic
    public void IsLithuanian_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.IsLithuanian(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Mägi", true)]  // Estonian surname
    [InlineData("Põld", true)]  // Estonian unique letter õ
    [InlineData("Tammsaar", true)]  // Estonian pattern
    [InlineData("Smith", false)]
    public void IsEstonian_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.IsEstonian(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Kowalski", false)]  // No Polish special chars
    [InlineData("Wójcik", true)]  // Polish special character
    [InlineData("Nowak", false)]  // Common but no special chars
    [InlineData("Żółć", true)]  // Polish special characters
    public void IsPolish_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.IsPolish(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Müller", true)]  // German umlaut
    [InlineData("Weiß", true)]  // German eszett
    [InlineData("Schmidt", false)]  // German but no special chars
    public void IsGerman_ShouldDetectCorrectly(string? input, bool expected)
    {
        var result = ScriptDetector.IsGerman(input);
        result.Should().Be(expected);
    }

    #endregion

    #region Text Splitting

    [Fact]
    public void SplitByScript_ShouldSplitMixedText()
    {
        var result = ScriptDetector.SplitByScript("Петров Petrov");

        result.Should().ContainKey(ScriptDetector.TextScript.Cyrillic);
        result.Should().ContainKey(ScriptDetector.TextScript.Latin);
        result[ScriptDetector.TextScript.Cyrillic].Should().Be("Петров");
        result[ScriptDetector.TextScript.Latin].Should().Be("Petrov");
    }

    [Fact]
    public void SplitByScript_ShouldHandleSlashSeparator()
    {
        var result = ScriptDetector.SplitByScript("Иванов/Ivanov");

        result.Should().ContainKey(ScriptDetector.TextScript.Cyrillic);
        result.Should().ContainKey(ScriptDetector.TextScript.Latin);
    }

    [Fact]
    public void SplitByScript_ShouldHandleParentheses()
    {
        var result = ScriptDetector.SplitByScript("Петров (Petrov)");

        result.Should().ContainKey(ScriptDetector.TextScript.Cyrillic);
        result.Should().ContainKey(ScriptDetector.TextScript.Latin);
        result[ScriptDetector.TextScript.Cyrillic].Should().Be("Петров");
        result[ScriptDetector.TextScript.Latin].Should().Be("Petrov");
    }

    [Fact]
    public void SplitByScript_ShouldReturnEmptyForPureScript()
    {
        var result = ScriptDetector.SplitByScript("Smith");

        result.Should().ContainKey(ScriptDetector.TextScript.Latin);
        result.Should().NotContainKey(ScriptDetector.TextScript.Cyrillic);
    }

    [Fact]
    public void SplitByScript_ShouldHandleNull()
    {
        var result = ScriptDetector.SplitByScript(null);
        result.Should().BeEmpty();
    }

    #endregion

    #region Character Classification

    [Theory]
    [InlineData('а', true)]
    [InlineData('Я', true)]
    [InlineData('ё', true)]
    [InlineData('і', true)]  // Ukrainian
    [InlineData('a', false)]
    [InlineData('Z', false)]
    public void IsCyrillic_ShouldClassifyCorrectly(char c, bool expected)
    {
        var result = ScriptDetector.IsCyrillic(c);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('ä', true)]  // Extended Latin
    [InlineData('ł', true)]  // Polish
    [InlineData('а', false)]  // Cyrillic
    public void IsLatin_ShouldClassifyCorrectly(char c, bool expected)
    {
        var result = ScriptDetector.IsLatin(c);
        result.Should().Be(expected);
    }

    #endregion
}
