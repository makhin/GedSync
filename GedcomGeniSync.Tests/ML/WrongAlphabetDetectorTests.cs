using GedcomGeniSync.Services.ML;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GedcomGeniSync.Tests.ML;

public class WrongAlphabetDetectorTests
{
    private readonly WrongAlphabetDetector _detector;

    public WrongAlphabetDetectorTests()
    {
        _detector = new WrongAlphabetDetector(NullLogger.Instance);
    }

    #region Russian names in Latin (transliterated)

    [Theory]
    [InlineData("Ivan", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Dmitry", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Sergey", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Mikhail", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Natalia", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Ekaterina", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Ivanov", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Petrov", WrongAlphabetDetector.NameOrigin.Russian)]
    [InlineData("Kuznetsov", WrongAlphabetDetector.NameOrigin.Russian)]
    public void Detect_KnownRussianNameInLatin_ReturnsRussianOrigin(string name, WrongAlphabetDetector.NameOrigin expectedOrigin)
    {
        var result = _detector.Detect(name);

        Assert.Equal(expectedOrigin, result.Origin);
        Assert.Equal(NameScript.Latin, result.ActualScript);
        Assert.True(result.Confidence > 0.8f);
    }

    [Theory]
    [InlineData("Ivan", "Иван")]
    [InlineData("Dmitry", "Дмитрий")]
    [InlineData("Sergey", "Сергей")]
    [InlineData("Mikhail", "Михаил")]
    [InlineData("Natalia", "Наталья")]
    [InlineData("Ekaterina", "Екатерина")]
    [InlineData("Ivanov", "Иванов")]
    [InlineData("Petrova", "Петрова")]
    public void Detect_KnownRussianNameInLatin_ReturnsCyrillicCorrection(string name, string expectedCyrillic)
    {
        var result = _detector.Detect(name);

        Assert.Equal(expectedCyrillic, result.SuggestedCorrection);
    }

    [Theory]
    [InlineData("Aleksandrovich")]  // Patronymic pattern -ovich
    [InlineData("Nikolaevna")]      // Patronymic pattern -evna
    [InlineData("Chernyshevsky")]   // Pattern -sky + sh
    [InlineData("Zhukovsky")]       // Pattern zh + sky
    [InlineData("Khrushchev")]      // Patterns kh + shch
    public void Detect_RussianTranslitPatterns_ReturnsRussianOrigin(string name)
    {
        var result = _detector.Detect(name);

        Assert.Equal(WrongAlphabetDetector.NameOrigin.Russian, result.Origin);
        Assert.Equal(NameScript.Latin, result.ActualScript);
        Assert.Contains("pattern", result.Reason.ToLower());
    }

    #endregion

    #region English names in Cyrillic

    [Theory]
    [InlineData("Майкл", "Michael")]
    [InlineData("Джон", "John")]
    [InlineData("Дэвид", "David")]
    [InlineData("Дженнифер", "Jennifer")]
    [InlineData("Элизабет", "Elizabeth")]
    public void Detect_KnownEnglishNameInCyrillic_ReturnsEnglishOriginWithCorrection(string name, string expectedCorrection)
    {
        var result = _detector.Detect(name);

        Assert.Equal(WrongAlphabetDetector.NameOrigin.English, result.Origin);
        Assert.Equal(NameScript.Cyrillic, result.ActualScript);
        Assert.Equal(expectedCorrection, result.SuggestedCorrection);
        Assert.True(result.Confidence > 0.9f);
    }

    [Theory]
    [InlineData("Джейсон")]   // Starts with Дж
    [InlineData("Джордж")]    // Starts with Дж
    [InlineData("Джеймс")]    // Starts with Дж
    public void Detect_EnglishCyrillicPatterns_ReturnsEnglishOrigin(string name)
    {
        var result = _detector.Detect(name);

        Assert.Equal(WrongAlphabetDetector.NameOrigin.English, result.Origin);
        Assert.Equal(NameScript.Cyrillic, result.ActualScript);
    }

    #endregion

    #region Correct scripts

    [Theory]
    [InlineData("Michael", WrongAlphabetDetector.NameOrigin.English)]
    [InlineData("John", WrongAlphabetDetector.NameOrigin.English)]
    [InlineData("David", WrongAlphabetDetector.NameOrigin.English)]
    [InlineData("Jennifer", WrongAlphabetDetector.NameOrigin.English)]
    [InlineData("Smith", WrongAlphabetDetector.NameOrigin.English)]
    public void Detect_EnglishNameInLatin_ReturnsEnglishOrigin(string name, WrongAlphabetDetector.NameOrigin expectedOrigin)
    {
        var result = _detector.Detect(name);

        Assert.Equal(expectedOrigin, result.Origin);
        Assert.Equal(NameScript.Latin, result.ActualScript);
        Assert.Null(result.SuggestedCorrection);
    }

    [Theory]
    [InlineData("Иван")]
    [InlineData("Мария")]
    [InlineData("Сергей")]
    [InlineData("Петров")]
    public void Detect_RussianNameInCyrillic_ReturnsRussianOrigin(string name)
    {
        var result = _detector.Detect(name);

        Assert.Equal(WrongAlphabetDetector.NameOrigin.Russian, result.Origin);
        Assert.Equal(NameScript.Cyrillic, result.ActualScript);
        Assert.Null(result.SuggestedCorrection);
    }

    #endregion

    #region Edge cases

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Detect_EmptyOrNull_ReturnsUnknown(string? name)
    {
        var result = _detector.Detect(name!);

        Assert.Equal(WrongAlphabetDetector.NameOrigin.Unknown, result.Origin);
    }

    [Fact]
    public void Detect_MixedScript_ReturnsUnknown()
    {
        var result = _detector.Detect("IvanИван");

        Assert.Equal(WrongAlphabetDetector.NameOrigin.Unknown, result.Origin);
        Assert.Equal(NameScript.Mixed, result.ActualScript);
    }

    #endregion

    #region ML Training

    [Fact]
    public void GenerateTrainingData_ReturnsData()
    {
        var trainingData = _detector.GenerateTrainingData().ToList();

        Assert.NotEmpty(trainingData);
        Assert.Contains(trainingData, d => d.Category == "ru-native");
        Assert.Contains(trainingData, d => d.Category == "ru-translit");
        Assert.Contains(trainingData, d => d.Category == "en-native");
        Assert.Contains(trainingData, d => d.Category == "en-cyrillic");
    }

    [Fact]
    public void Train_WithGeneratedData_Succeeds()
    {
        var trainingData = _detector.GenerateTrainingData();

        // Should not throw
        _detector.Train(trainingData);
    }

    [Fact]
    public void Detect_AfterTraining_UsesModel()
    {
        // Train the model
        var trainingData = _detector.GenerateTrainingData();
        _detector.Train(trainingData);

        // Test detection - should use ML model now
        var result = _detector.Detect("Vladimirovich");

        Assert.Equal(WrongAlphabetDetector.NameOrigin.Russian, result.Origin);
    }

    #endregion
}
