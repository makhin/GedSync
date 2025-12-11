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

    [Fact]
    public void LoadFromCsv_ShouldParseActualCsvFormat()
    {
        var service = new NameVariantsService(NullLogger<NameVariantsService>.Instance);

        // Create a test CSV file with the actual format from tfmorris/Names
        var testCsvPath = Path.Combine(Path.GetTempPath(), "test_names.csv");
        File.WriteAllText(testCsvPath, "name,similar_names\n\"john\",\"ean eoin evan gianni giovanni ivan jack jamie jan jean\"");

        try
        {
            service.LoadFromCsv(testCsvPath, testCsvPath);

            // Test if variants were loaded correctly
            // With pipe-split (WRONG): would get 1 variant "ean eoin evan..."
            // With space-split (CORRECT): would get multiple variants "ean", "eoin", "evan"...

            // This SHOULD be true if parsed correctly:
            service.AreEquivalent("john", "evan").Should().BeTrue();
            service.AreEquivalent("john", "ivan").Should().BeTrue();
            service.AreEquivalent("john", "jack").Should().BeTrue();
        }
        finally
        {
            if (File.Exists(testCsvPath))
                File.Delete(testCsvPath);
        }
    }
}
