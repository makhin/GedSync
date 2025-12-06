using FluentAssertions;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GedcomGeniSync.Tests;

public class NameVariantsServiceTests
{
    [Fact]
    public void AreEquivalent_ShouldRecognizeBuiltInVariants()
    {
        var service = new NameVariantsService(NullLogger<NameVariantsService>.Instance);

        service.AreEquivalent("Иван", "John").Should().BeTrue();
    }

    [Fact]
    public void AreEquivalentSurnames_ShouldUseTransliteration()
    {
        var service = new NameVariantsService(NullLogger<NameVariantsService>.Instance);

        service.AreEquivalentSurnames("Петров", "Petrov").Should().BeTrue();
    }

    [Fact]
    public void AddSurnameVariants_ShouldAddBidirectionalMappings()
    {
        var service = new NameVariantsService(NullLogger<NameVariantsService>.Instance);

        service.AddSurnameVariants("Smith", new[] { "Smyth" });

        service.AreEquivalentSurnames("Smith", "Smyth").Should().BeTrue();
        service.AreEquivalentSurnames("Smyth", "Smith").Should().BeTrue();
    }

    [Fact]
    public void LoadFromCsv_ShouldIgnoreMissingFiles()
    {
        var service = new NameVariantsService(NullLogger<NameVariantsService>.Instance);

        Action act = () => service.LoadFromCsv("/tmp/does-not-exist.csv", "/tmp/also-missing.csv");

        act.Should().NotThrow();
    }
}
