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
        context.GetName(Locales.PreferredEnglish, NameFields.FirstName).Should().Be("Mariya");
        context.GetName(Locales.PreferredEnglish, NameFields.LastName).Should().Be("Popova");
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
