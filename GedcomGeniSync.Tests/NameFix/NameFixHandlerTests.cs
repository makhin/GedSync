using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services.NameFix;
using GedcomGeniSync.Services.NameFix.Handlers;

namespace GedcomGeniSync.Tests.NameFix;

public class NameFixHandlerTests
{
    #region ScriptSplitHandler Tests

    [Fact]
    public void ScriptSplitHandler_ShouldSplitMixedScriptValue()
    {
        // Arrange
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Петров Petrov"
        };

        var handler = new ScriptSplitHandler();

        // Act
        handler.Handle(context);

        // Assert
        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Петров");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Petrov");
    }

    [Fact]
    public void ScriptSplitHandler_ShouldHandleParenthesesFormat()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван (Ivan)"
        };

        var handler = new ScriptSplitHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Ivan");
    }

    [Fact]
    public void ScriptSplitHandler_ShouldNotModifyPureScript()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "John"
        };

        var handler = new ScriptSplitHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
    }

    #endregion

    #region CyrillicToRuHandler Tests

    [Fact]
    public void CyrillicToRuHandler_ShouldMoveCyrillicFromEnToRu()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = "Петров"
        };

        var handler = new CyrillicToRuHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Петров");
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().BeNull();
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().BeNull();
    }

    [Fact]
    public void CyrillicToRuHandler_ShouldNotMoveLatinFromEn()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Ivan"
        };

        var handler = new CyrillicToRuHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Ivan");
    }

    [Fact]
    public void CyrillicToRuHandler_ShouldRemoveDuplicateIfRuAlreadyHas()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван"
        };
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван"
        };

        var handler = new CyrillicToRuHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().BeNull();
        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
    }

    #endregion

    #region TranslitHandler Tests

    [Fact]
    public void TranslitHandler_ShouldGenerateTranslitFromRu()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = "Петров"
        };

        var handler = new TranslitHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Ivan");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Petrov");
    }

    [Fact]
    public void TranslitHandler_ShouldNotOverwriteExistingEn()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван"
        };
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "John"  // Custom English name
        };

        var handler = new TranslitHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("John");
    }

    #endregion

    #region FeminineSurnameHandler Tests

    [Fact]
    public void FeminineSurnameHandler_ShouldFixMasculineToFeminine()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Попов"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Попова");
    }

    [Fact]
    public void FeminineSurnameHandler_ShouldNotChangeMaleNames()
    {
        var context = CreateContext(Gender.Male);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Попов"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Попов");
    }

    [Fact]
    public void FeminineSurnameHandler_ShouldNotChangeAlreadyFeminine()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Попова"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FeminineSurnameHandler_ShouldNotChangeUkrainianKo()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Шевченко"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Шевченко");
    }

    [Fact]
    public void FeminineSurnameHandler_ShouldHandleAdjectiveSurnames()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Чайковский"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Чайковская");
    }

    [Fact]
    public void FeminineSurnameHandler_ShouldHandleTransliterated()
    {
        var context = CreateContext(Gender.Female);
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Popov"
        };

        var handler = new FeminineSurnameHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Popova");
    }

    #endregion

    #region LithuanianHandler Tests

    [Fact]
    public void LithuanianHandler_ShouldDetectAndCopyToLt()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Jonaitis"
        };

        var handler = new LithuanianHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.Lithuanian, NameFields.LastName).Should().Be("Jonaitis");
    }

    [Fact]
    public void LithuanianHandler_ShouldDetectSpecialChars()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Šimkus"
        };

        var handler = new LithuanianHandler();
        handler.Handle(context);

        context.GetName(Locales.Lithuanian, NameFields.LastName).Should().Be("Šimkus");
    }

    #endregion

    #region EstonianHandler Tests

    [Fact]
    public void EstonianHandler_ShouldDetectOWithTilde()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Põld"
        };

        var handler = new EstonianHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.GetName(Locales.Estonian, NameFields.LastName).Should().Be("Põld");
    }

    #endregion

    #region CleanupHandler Tests

    [Fact]
    public void CleanupHandler_ShouldRemoveEmptyFields()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = ""
        };

        var handler = new CleanupHandler();
        handler.Handle(context);

        context.Names["ru"].Should().ContainKey("first_name");
        context.Names["ru"].Should().NotContainKey("last_name");
    }

    [Fact]
    public void CleanupHandler_ShouldRemoveEmptyLocales()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван"
        };
        context.Names["de"] = new Dictionary<string, string>();

        var handler = new CleanupHandler();
        handler.Handle(context);

        context.Names.Should().ContainKey("ru");
        context.Names.Should().NotContainKey("de");
    }

    [Fact]
    public void CleanupHandler_ShouldTrimWhitespace()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "  Иван  "
        };

        var handler = new CleanupHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
    }

    #endregion

    #region EnsureEnglishHandler Tests

    [Fact]
    public void EnsureEnglishHandler_ShouldPopulateFromRussian()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = "Петров"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Ivan");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Petrov");
    }

    [Fact]
    public void EnsureEnglishHandler_ShouldSimplifyDiacritics()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Müller",
            ["last_name"] = "Šimkus"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Muller");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Simkus");
    }

    [Fact]
    public void EnsureEnglishHandler_ShouldPopulateFromLithuanianWithSimplification()
    {
        var context = CreateContext();
        context.Names["lt"] = new Dictionary<string, string>
        {
            ["last_name"] = "Kazlauskaitė"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Kazlauskaite");
    }

    [Fact]
    public void EnsureEnglishHandler_ShouldNotOverwriteExistingBasicLatin()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "John"
        };
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Джон"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        // Should keep existing English, not replace with transliteration
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("John");
    }

    [Fact]
    public void EnsureEnglishHandler_ShouldHandleGermanEszett()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Weiß"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Weiss");
    }

    [Fact]
    public void EnsureEnglishHandler_ShouldHandlePolishL()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Łukasz"
        };

        var handler = new EnsureEnglishHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Lukasz");
    }

    #endregion

    #region Pipeline Tests

    [Fact]
    public void Pipeline_ShouldProcessInOrder()
    {
        var context = CreateContext(Gender.Female);
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Мария",
            ["last_name"] = "Попов"
        };

        // Create pipeline with handlers
        var handlers = new INameFixHandler[]
        {
            new CyrillicToRuHandler(),
            new TranslitHandler(),
            new FeminineSurnameHandler(),
            new CleanupHandler()
        };

        foreach (var handler in handlers.OrderBy(h => h.Order))
        {
            handler.Handle(context);
        }

        // Verify final state
        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Мария");
        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Попова");
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Marija");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Popova");
    }

    #endregion

    #region SpecialCharsCleanupHandler Tests

    [Fact]
    public void SpecialCharsCleanupHandler_ShouldRemoveAsterisk()
    {
        var context = CreateContext();
        context.FirstName = "Иван*";

        var handler = new SpecialCharsCleanupHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Иван");
    }

    [Fact]
    public void SpecialCharsCleanupHandler_ShouldRemoveQuestionMark()
    {
        var context = CreateContext();
        context.LastName = "Петров?";

        var handler = new SpecialCharsCleanupHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Петров");
    }

    [Fact]
    public void SpecialCharsCleanupHandler_ShouldRemoveLeadingNumbers()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "123 John"
        };

        var handler = new SpecialCharsCleanupHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("John");
    }

    #endregion

    #region TitleExtractHandler Tests

    [Fact]
    public void TitleExtractHandler_ShouldExtractDr()
    {
        var context = CreateContext();
        context.FirstName = "Dr. John";

        var handler = new TitleExtractHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("John");
        // Title is recorded in changes, not in a separate property
        context.Changes.Should().Contain(c => c.Reason.Contains("title") || c.Reason.Contains("Title"));
    }

    [Fact]
    public void TitleExtractHandler_ShouldExtractRussianTitle()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "князь Иван"
        };

        var handler = new TitleExtractHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
        context.GetName(Locales.Russian, NameFields.Title).Should().Be("князь");
    }

    #endregion

    #region SuffixExtractHandler Tests

    [Fact]
    public void SuffixExtractHandler_ShouldExtractJr()
    {
        var context = CreateContext();
        context.LastName = "Smith Jr.";

        var handler = new SuffixExtractHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Smith");
        context.Suffix.Should().Be("Jr.");
    }

    [Fact]
    public void SuffixExtractHandler_ShouldExtractIII()
    {
        var context = CreateContext();
        context.LastName = "John III";

        var handler = new SuffixExtractHandler();
        handler.Handle(context);

        context.LastName.Should().Be("John");
        context.Suffix.Should().Be("III");
    }

    #endregion

    #region MaidenNameExtractHandler Tests

    [Fact]
    public void MaidenNameExtractHandler_ShouldExtractFromParentheses()
    {
        var context = CreateContext(Gender.Female);
        context.LastName = "Иванова (Петрова)";

        var handler = new MaidenNameExtractHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Иванова");
        context.MaidenName.Should().Be("Петрова");
    }

    [Fact]
    public void MaidenNameExtractHandler_ShouldExtractNee()
    {
        var context = CreateContext(Gender.Female);
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["last_name"] = "Smith née Jones"
        };

        var handler = new MaidenNameExtractHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Smith");
        // Maiden name is extracted to the locale's maiden_name field
        context.GetName(Locales.PreferredEnglish, NameFields.MaidenName).Should().Be("Jones");
    }

    #endregion

    #region NicknameExtractHandler Tests

    [Fact]
    public void NicknameExtractHandler_ShouldExtractFromQuotes()
    {
        var context = CreateContext();
        context.FirstName = "Александр \"Саша\"";

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Александр");
        context.Nicknames.Should().Be("Саша");
    }

    [Fact]
    public void NicknameExtractHandler_ShouldExtractFromParentheses()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "William (Bill)"
        };

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("William");
        context.Nicknames.Should().Be("Bill");
    }

    [Fact]
    public void NicknameExtractHandler_ShouldExtractAnyParenthesesContent()
    {
        // Нина (Серафима) - not a known diminutive but should still extract
        var context = CreateContext();
        context.FirstName = "Нина (Серафима)";

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Нина");
        context.Nicknames.Should().Be("Серафима");
    }

    [Fact]
    public void NicknameExtractHandler_ShouldExtractMultipleNicknames()
    {
        // Александр (Шура, Саша) - multiple nicknames
        var context = CreateContext();
        context.FirstName = "Александр (Шура, Саша)";

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Александр");
        context.Nicknames.Should().Be("Шура, Саша");
    }

    [Fact]
    public void NicknameExtractHandler_ShouldExtractFromPrimaryAndLocale()
    {
        var context = CreateContext();
        context.FirstName = "Александр (Саша)";
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Alexander (Alex)"
        };

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Александр");
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Alexander");
        // Should have both nicknames, deduplicated
        context.Nicknames.Should().Contain("Саша");
        context.Nicknames.Should().Contain("Alex");
    }

    [Fact]
    public void NicknameExtractHandler_ShouldNotExtractEmptyParentheses()
    {
        var context = CreateContext();
        context.FirstName = "Александр ()";

        var handler = new NicknameExtractHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.FirstName.Should().Be("Александр ()");
    }

    #endregion

    #region PatronymicHandler Tests

    [Fact]
    public void PatronymicHandler_ShouldDetectMalePatronymic()
    {
        var context = CreateContext(Gender.Male);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван Петрович"
        };

        var handler = new PatronymicHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
        context.GetName(Locales.Russian, NameFields.MiddleName).Should().Be("Петрович");
    }

    [Fact]
    public void PatronymicHandler_ShouldDetectFemalePatronymic()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Мария Ивановна"
        };

        var handler = new PatronymicHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Мария");
        context.GetName(Locales.Russian, NameFields.MiddleName).Should().Be("Ивановна");
    }

    #endregion

    #region UkrainianHandler Tests

    [Fact]
    public void UkrainianHandler_ShouldDetectUkrainianI()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Олексій"  // Contains Ukrainian і
        };

        var handler = new UkrainianHandler();
        handler.Handle(context);

        context.GetName(Locales.Ukrainian, NameFields.FirstName).Should().Be("Олексій");
    }

    [Fact]
    public void UkrainianHandler_ShouldDetectEnkoSurname()
    {
        var context = CreateContext();
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Шевченко"
        };

        var handler = new UkrainianHandler();
        handler.Handle(context);

        context.GetName(Locales.Ukrainian, NameFields.LastName).Should().Be("Шевченко");
    }

    #endregion

    #region HebrewHandler Tests

    [Fact]
    public void HebrewHandler_ShouldDetectHebrewText()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "דוד"  // David in Hebrew
        };

        var handler = new HebrewHandler();
        handler.Handle(context);

        context.GetName(Locales.Hebrew, NameFields.FirstName).Should().Be("דוד");
    }

    [Fact]
    public void HebrewHandler_ShouldExtractHebrewFromMixed()
    {
        var context = CreateContext();
        context.FirstName = "דוד David";

        var handler = new HebrewHandler();
        handler.Handle(context);

        context.GetName(Locales.Hebrew, NameFields.FirstName).Should().Be("דוד");
    }

    #endregion

    #region SurnameParticleHandler Tests

    [Fact]
    public void SurnameParticleHandler_ShouldNormalizeVon()
    {
        var context = CreateContext();
        context.LastName = "VON NEUMANN";

        var handler = new SurnameParticleHandler();
        handler.Handle(context);

        context.LastName.Should().Be("von Neumann");
    }

    [Fact]
    public void SurnameParticleHandler_ShouldNormalizeMcDonald()
    {
        var context = CreateContext();
        context.LastName = "mcdonald";

        var handler = new SurnameParticleHandler();
        handler.Handle(context);

        context.LastName.Should().Be("McDonald");
    }

    [Fact]
    public void SurnameParticleHandler_ShouldNormalizeOBrien()
    {
        var context = CreateContext();
        context.LastName = "o'brien";

        var handler = new SurnameParticleHandler();
        handler.Handle(context);

        context.LastName.Should().Be("O'Brien");
    }

    #endregion

    #region CapitalizationHandler Tests

    [Fact]
    public void CapitalizationHandler_ShouldFixAllCaps()
    {
        var context = CreateContext();
        context.FirstName = "JOHN";
        context.LastName = "SMITH";

        var handler = new CapitalizationHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("John");
        context.LastName.Should().Be("Smith");
    }

    [Fact]
    public void CapitalizationHandler_ShouldFixAllLowercase()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "john",
            ["last_name"] = "smith"
        };

        var handler = new CapitalizationHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("John");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Smith");
    }

    [Fact]
    public void CapitalizationHandler_ShouldHandleHyphenatedNames()
    {
        var context = CreateContext();
        context.FirstName = "ANNA-MARIA";

        var handler = new CapitalizationHandler();
        handler.Handle(context);

        context.FirstName.Should().Be("Anna-Maria");
    }

    #endregion

    #region DuplicateRemovalHandler Tests

    [Fact]
    public void DuplicateRemovalHandler_ShouldRemoveExactDuplicates()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "John"
        };
        context.Names["de"] = new Dictionary<string, string>
        {
            ["first_name"] = "John"  // Same value, lower priority locale
        };

        var handler = new DuplicateRemovalHandler();
        handler.Handle(context);

        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("John");
        context.GetName("de", NameFields.FirstName).Should().BeNull();
    }

    [Fact]
    public void DuplicateRemovalHandler_ShouldNotRemoveDifferentScripts()
    {
        var context = CreateContext();
        context.Names["en-US"] = new Dictionary<string, string>
        {
            ["first_name"] = "Ivan"
        };
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван"  // Same name, different script - keep both
        };

        var handler = new DuplicateRemovalHandler();
        handler.Handle(context);

        // Both should remain as they're in different scripts
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Ivan");
        context.GetName(Locales.Russian, NameFields.FirstName).Should().Be("Иван");
    }

    #endregion

    #region MarriedSurnameHandler Tests

    [Fact]
    public void MarriedSurnameHandler_ShouldSwapWhenMaidenMatchesSpouse()
    {
        // Woman has: LastName="Попова", MaidenName="Рыжова"
        // Husband: LastName="Рыжов"
        // Expected: LastName="Рыжова", MaidenName="Попова"
        var context = CreateContext(Gender.Female);
        context.LastName = "Попова";
        context.MaidenName = "Рыжова";
        context.SpouseLastName = "Рыжов";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Рыжова");
        context.MaidenName.Should().Be("Попова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldNotSwapWhenLastNameMatchesSpouse()
    {
        // Woman has: LastName="Попова", MaidenName="Рыжова"
        // Husband: LastName="Попов"
        // Expected: No change (already correct)
        var context = CreateContext(Gender.Female);
        context.LastName = "Попова";
        context.MaidenName = "Рыжова";
        context.SpouseLastName = "Попов";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.LastName.Should().Be("Попова");
        context.MaidenName.Should().Be("Рыжова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldHandleTransliteratedSurnames()
    {
        // Woman has: LastName="Popova", MaidenName="Ryzhova"
        // Husband: LastName="Ryzhov"
        // Expected: LastName="Ryzhova", MaidenName="Popova"
        var context = CreateContext(Gender.Female);
        context.LastName = "Popova";
        context.MaidenName = "Ryzhova";
        context.SpouseLastName = "Ryzhov";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Ryzhova");
        context.MaidenName.Should().Be("Popova");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldSwapLocaleFields()
    {
        var context = CreateContext(Gender.Female);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Попова",
            ["maiden_name"] = "Рыжова"
        };
        context.SpouseLastNames = new Dictionary<string, string>
        {
            ["ru"] = "Рыжов"
        };

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.LastName).Should().Be("Рыжова");
        context.GetName(Locales.Russian, NameFields.MaidenName).Should().Be("Попова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldNotProcessMales()
    {
        var context = CreateContext(Gender.Male);
        context.LastName = "Попов";
        context.MaidenName = "Рыжов";
        context.SpouseLastName = "Рыжова";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldNotProcessWithoutSpouseInfo()
    {
        var context = CreateContext(Gender.Female);
        context.LastName = "Попова";
        context.MaidenName = "Рыжова";
        // No SpouseLastName

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldHandleAdjectiveSurnames()
    {
        // Adjective-style surnames: Чайковский → Чайковская
        var context = CreateContext(Gender.Female);
        context.LastName = "Чайковская";
        context.MaidenName = "Рыжова";
        context.SpouseLastName = "Рыжов";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Рыжова");
        context.MaidenName.Should().Be("Чайковская");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldHandleInSuffix()
    {
        // -ин → -ина: Путин → Путина
        var context = CreateContext(Gender.Female);
        context.LastName = "Иванова";
        context.MaidenName = "Путина";
        context.SpouseLastName = "Путин";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Путина");
        context.MaidenName.Should().Be("Иванова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldHandleUkrainianKoUnchanged()
    {
        // Ukrainian -ко surnames don't change by gender
        var context = CreateContext(Gender.Female);
        context.LastName = "Иванова";
        context.MaidenName = "Шевченко";
        context.SpouseLastName = "Шевченко";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.LastName.Should().Be("Шевченко");
        context.MaidenName.Should().Be("Иванова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldCopyLastNameToMaidenNameForMale()
    {
        // Male with LastName but no MaidenName
        var context = CreateContext(Gender.Male);
        context.LastName = "Попов";
        // MaidenName is null

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.MaidenName.Should().Be("Попов");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldCopyLastNameToMaidenNameForUnmarriedFemale()
    {
        // Female without spouse info (unmarried)
        var context = CreateContext(Gender.Female);
        context.LastName = "Иванова";
        // No SpouseLastName, MaidenName is null

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeTrue();
        context.MaidenName.Should().Be("Иванова");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldNotOverwriteExistingMaidenName()
    {
        // Male with existing MaidenName (should not overwrite)
        var context = CreateContext(Gender.Male);
        context.LastName = "Попов";
        context.MaidenName = "Другая";

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.MaidenName.Should().Be("Другая");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldCopyLocaleLastNameToMaidenName()
    {
        // Male with locale LastName but no MaidenName in that locale
        var context = CreateContext(Gender.Male);
        context.Names["ru"] = new Dictionary<string, string>
        {
            ["last_name"] = "Петров"
        };

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.GetName(Locales.Russian, NameFields.MaidenName).Should().Be("Петров");
    }

    [Fact]
    public void MarriedSurnameHandler_ShouldNotCopyIfLastNameEmpty()
    {
        // Male without LastName
        var context = CreateContext(Gender.Male);
        // LastName is null

        var handler = new MarriedSurnameHandler();
        handler.Handle(context);

        context.IsDirty.Should().BeFalse();
        context.MaidenName.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static NameFixContext CreateContext(Gender gender = Gender.Unknown)
    {
        return new NameFixContext
        {
            ProfileId = "test-profile-123",
            DisplayName = "Test Person",
            Gender = gender
        };
    }

    #endregion
}
