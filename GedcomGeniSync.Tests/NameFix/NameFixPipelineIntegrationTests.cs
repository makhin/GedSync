using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services.NameFix;
using GedcomGeniSync.Services.NameFix.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GedcomGeniSync.Tests.NameFix;

/// <summary>
/// Integration tests for the complete NameFix pipeline.
/// These tests run all handlers in sequence to verify end-to-end behavior.
/// </summary>
public class NameFixPipelineIntegrationTests
{
    private readonly NameFixPipeline _pipeline;

    public NameFixPipelineIntegrationTests()
    {
        // Create all handlers in the correct order
        var handlers = new INameFixHandler[]
        {
            new SpecialCharsCleanupHandler(),      // Order: 5
            new TitleExtractHandler(),              // Order: 8
            new ScriptSplitHandler(),               // Order: 10
            new SuffixExtractHandler(),             // Order: 11
            new MaidenNameExtractHandler(),         // Order: 12
            new NicknameExtractHandler(),           // Order: 13
            new PatronymicHandler(),                // Order: 15
            new CyrillicToRuHandler(),              // Order: 20
            new UkrainianHandler(),                 // Order: 24
            new LithuanianHandler(),                // Order: 25
            new EstonianHandler(),                  // Order: 26
            new LatinLanguageHandler(),             // Order: 27
            new HebrewHandler(),                    // Order: 28
            new TranslitHandler(),                  // Order: 30
            new EnsureEnglishHandler(),             // Order: 35
            new FeminineSurnameHandler(),           // Order: 40
            new SurnameParticleHandler(),           // Order: 42
            new CapitalizationHandler(),            // Order: 95
            new DuplicateRemovalHandler(),          // Order: 98
            new CleanupHandler()                    // Order: 100
        };

        _pipeline = new NameFixPipeline(handlers, NullLogger<NameFixPipeline>.Instance);
    }

    #region Test 1: Russian Name with Cyrillic in English Locale

    /// <summary>
    /// Test: Russian name incorrectly placed in en-US locale.
    /// Expected: Move to ru, create transliteration in en-US.
    /// </summary>
    [Fact]
    public void Test01_RussianNameInEnglishLocale_ShouldMoveToRuAndTransliterate()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = "Петров"
        };

        // Act
        _pipeline.Process(context);

        // Assert
        // Russian should be in ru locale
        context.GetName("ru", "first_name").Should().Be("Иван");
        context.GetName("ru", "last_name").Should().Be("Петров");

        // English should have transliteration (basic Latin)
        var enFirstName = context.GetName("en-US", "first_name");
        var enLastName = context.GetName("en-US", "last_name");

        enFirstName.Should().NotBeNullOrEmpty();
        enLastName.Should().NotBeNullOrEmpty();

        // Should be basic Latin (no Cyrillic)
        enFirstName.Should().MatchRegex("^[A-Za-z]+$");
        enLastName.Should().MatchRegex("^[A-Za-z]+$");

        context.IsDirty.Should().BeTrue();
    }

    #endregion

    #region Test 2: Mixed Script Name Split

    /// <summary>
    /// Test: Name contains both Cyrillic and Latin in single field.
    /// Expected: Split into appropriate locales.
    /// </summary>
    [Fact]
    public void Test02_MixedScriptName_ShouldSplitByScript()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.FirstName = "Иван Ivan";
        context.LastName = "Петров Petrov";

        // Act
        _pipeline.Process(context);

        // Assert
        // Russian in ru locale
        context.GetName("ru", "first_name").Should().Be("Иван");
        context.GetName("ru", "last_name").Should().Be("Петров");

        // English in en-US
        context.GetName("en-US", "first_name").Should().Be("Ivan");
        context.GetName("en-US", "last_name").Should().Be("Petrov");

        // Primary fields should be Latin
        context.FirstName.Should().Be("Ivan");
        context.LastName.Should().Be("Petrov");
    }

    #endregion

    #region Test 3: Female Russian Surname Normalization

    /// <summary>
    /// Test: Female with masculine surname form.
    /// Expected: Surname should be feminized.
    /// </summary>
    [Fact]
    public void Test03_FemaleSurname_ShouldBeFeminized()
    {
        // Arrange
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Мария",
            ["last_name"] = "Иванов"  // Masculine form
        };

        // Act
        _pipeline.Process(context);

        // Assert
        context.GetName("ru", "last_name").Should().Be("Иванова");  // Feminine form
    }

    #endregion

    #region Test 4: Ukrainian Name Detection

    /// <summary>
    /// Test: Ukrainian name with specific characters.
    /// Expected: Should be detected and placed in uk locale.
    /// </summary>
    [Fact]
    public void Test04_UkrainianName_ShouldBeDetected()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Олексій",  // Contains Ukrainian 'і'
            ["last_name"] = "Шевченко"   // Ukrainian surname pattern
        };

        // Act
        _pipeline.Process(context);

        // Assert
        context.GetName("uk", "first_name").Should().Be("Олексій");
        context.GetName("uk", "last_name").Should().Be("Шевченко");
    }

    #endregion

    #region Test 5: Lithuanian Name with Diacritics

    /// <summary>
    /// Test: Lithuanian name with special characters.
    /// Expected: Keep diacritics in lt locale, simplify in en-US.
    /// </summary>
    [Fact]
    public void Test05_LithuanianName_ShouldPreserveDiacriticsInLocale()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Vytautas",
            ["last_name"] = "Šimkauskas"  // Lithuanian diacritics
        };

        // Act
        _pipeline.Process(context);

        // Assert
        // Lithuanian locale should have original with diacritics
        context.GetName("lt", "last_name").Should().Be("Šimkauskas");

        // English should have simplified version
        var enLastName = context.GetName("en-US", "last_name");
        enLastName.Should().NotContain("š");
        enLastName.Should().MatchRegex("^[A-Za-z]+$");
    }

    #endregion

    #region Test 6: Title and Suffix Extraction

    /// <summary>
    /// Test: Name with title and suffix embedded.
    /// Expected: Extract title and suffix to separate fields.
    /// </summary>
    [Fact]
    public void Test06_TitleAndSuffix_ShouldBeExtracted()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.FirstName = "Dr. John";
        context.LastName = "Smith Jr.";

        // Act
        _pipeline.Process(context);

        // Assert
        context.FirstName.Should().Be("John");
        context.LastName.Should().Be("Smith");
        context.Suffix.Should().Be("Jr.");
    }

    #endregion

    #region Test 7: Maiden Name Extraction

    /// <summary>
    /// Test: Female with maiden name in parentheses.
    /// Expected: Extract maiden name to separate field.
    /// </summary>
    [Fact]
    public void Test07_MaidenName_ShouldBeExtracted()
    {
        // Arrange
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Мария",
            ["last_name"] = "Иванова (Петрова)"  // Maiden name in parentheses
        };

        // Act
        _pipeline.Process(context);

        // Assert
        context.GetName("ru", "last_name").Should().Be("Иванова");
        context.MaidenName.Should().Be("Петрова");
    }

    #endregion

    #region Test 8: ALL CAPS Name Fix

    /// <summary>
    /// Test: Name in all caps.
    /// Expected: Convert to proper case.
    /// </summary>
    [Fact]
    public void Test08_AllCapsName_ShouldBeFixedToProperCase()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.FirstName = "JOHN";
        context.LastName = "SMITH";
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "JOHN",
            ["last_name"] = "SMITH"
        };

        // Act
        _pipeline.Process(context);

        // Assert
        context.FirstName.Should().Be("John");
        context.LastName.Should().Be("Smith");
        context.GetName("en-US", "first_name").Should().Be("John");
        context.GetName("en-US", "last_name").Should().Be("Smith");
    }

    #endregion

    #region Test 9: Surname Particle Normalization

    /// <summary>
    /// Test: Surname with particle (von, van, Mc, O').
    /// Expected: Proper capitalization of particles.
    /// </summary>
    [Fact]
    public void Test09_SurnameParticle_ShouldBeNormalized()
    {
        // Arrange
        var context = CreateContext(Gender.Male);
        context.LastName = "VON NEUMANN";

        // Act
        _pipeline.Process(context);

        // Assert
        context.LastName.Should().Be("von Neumann");
    }

    #endregion

    #region Test 10: Complex Real-World Scenario

    /// <summary>
    /// Test: Complex real-world scenario combining multiple issues.
    /// - Russian name with patronymic in wrong locale
    /// - Mixed with Latin transliteration
    /// - Female with masculine surname
    /// - Special characters
    /// </summary>
    [Fact]
    public void Test10_ComplexRealWorldScenario()
    {
        // Arrange
        var context = CreateContext(Gender.Female);
        context.FirstName = "*Мария Ивановна* Maria";  // Special chars, patronymic, mixed script
        context.LastName = "Петров Petrova";           // Mixed script, wrong gender for Cyrillic
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Мария",
            ["last_name"] = "Петров"
        };

        // Act
        _pipeline.Process(context);

        // Assert
        // Primary fields should be clean Latin
        context.FirstName.Should().NotContain("*");
        context.FirstName.Should().MatchRegex("^[A-Za-z]+$");

        // Russian locale should have proper names
        var ruFirstName = context.GetName("ru", "first_name");
        var ruLastName = context.GetName("ru", "last_name");

        ruFirstName.Should().NotBeNullOrEmpty();
        ruLastName.Should().NotBeNullOrEmpty();

        // Female surname should be feminized in Russian
        ruLastName.Should().EndWith("ова");

        // Middle name should have patronymic
        var ruMiddleName = context.GetName("ru", "middle_name");
        ruMiddleName.Should().Be("Ивановна");

        // English should be basic Latin
        var enFirstName = context.GetName("en-US", "first_name");
        var enLastName = context.GetName("en-US", "last_name");

        enFirstName.Should().MatchRegex("^[A-Za-z]+$");
        enLastName.Should().MatchRegex("^[A-Za-z]+$");

        // Multiple changes should be recorded
        context.Changes.Count.Should().BeGreaterThan(3);
    }

    #endregion

    #region Helpers

    private static NameFixContext CreateContext(Gender gender = Gender.Unknown)
    {
        return new NameFixContext
        {
            ProfileId = "test-profile-" + Guid.NewGuid().ToString("N")[..8],
            Gender = gender,
            Names = new Dictionary<string, Dictionary<string, string>>()
        };
    }

    #endregion
}
