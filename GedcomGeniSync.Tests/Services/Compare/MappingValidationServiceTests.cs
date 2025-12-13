using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Immutable;
using Xunit;

namespace GedcomGeniSync.Tests.Services.Compare;

public class MappingValidationServiceTests
{
    private readonly MappingValidationService _service;

    public MappingValidationServiceTests()
    {
        _service = new MappingValidationService(NullLogger<MappingValidationService>.Instance);
    }

    [Fact]
    public void ValidateMappings_GenderMismatch_ReturnsHighSeverityIssue()
    {
        // Arrange
        var sourcePerson = CreatePerson("@I1@", Gender.Male);
        var destPerson = CreatePerson("@I100@", Gender.Female);

        var mappings = new Dictionary<string, string> { { "@I1@", "@I100@" } };
        var sourcePersons = new Dictionary<string, PersonRecord> { { "@I1@", sourcePerson } };
        var destPersons = new Dictionary<string, PersonRecord> { { "@I100@", destPerson } };
        var sourceFamilies = new Dictionary<string, Family>();
        var destFamilies = new Dictionary<string, Family>();

        // Act
        var result = _service.ValidateMappings(
            mappings,
            sourcePersons,
            destPersons,
            sourceFamilies,
            destFamilies);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(1, result.HighSeverityCount);
        Assert.Contains(result.Issues, i => i.Type == IssueType.GenderMismatch);
    }

    [Fact]
    public void ValidateMappings_BirthYearDifferenceTooLarge_ReturnsMediumSeverityIssue()
    {
        // Arrange
        var sourcePerson = CreatePerson("@I1@", Gender.Male, birthYear: 1950);
        var destPerson = CreatePerson("@I100@", Gender.Male, birthYear: 1960);

        var mappings = new Dictionary<string, string> { { "@I1@", "@I100@" } };
        var sourcePersons = new Dictionary<string, PersonRecord> { { "@I1@", sourcePerson } };
        var destPersons = new Dictionary<string, PersonRecord> { { "@I100@", destPerson } };
        var sourceFamilies = new Dictionary<string, Family>();
        var destFamilies = new Dictionary<string, Family>();

        // Act
        var result = _service.ValidateMappings(
            mappings,
            sourcePersons,
            destPersons,
            sourceFamilies,
            destFamilies);

        // Assert
        Assert.Equal(1, result.MediumSeverityCount);
        Assert.Contains(result.Issues, i => i.Type == IssueType.DateContradiction);
    }

    [Fact]
    public void ValidateMappings_ValidMapping_ReturnsNoIssues()
    {
        // Arrange
        var sourcePerson = CreatePerson("@I1@", Gender.Male, birthYear: 1950);
        var destPerson = CreatePerson("@I100@", Gender.Male, birthYear: 1951);

        var mappings = new Dictionary<string, string> { { "@I1@", "@I100@" } };
        var sourcePersons = new Dictionary<string, PersonRecord> { { "@I1@", sourcePerson } };
        var destPersons = new Dictionary<string, PersonRecord> { { "@I100@", destPerson } };
        var sourceFamilies = new Dictionary<string, Family>();
        var destFamilies = new Dictionary<string, Family>();

        // Act
        var result = _service.ValidateMappings(
            mappings,
            sourcePersons,
            destPersons,
            sourceFamilies,
            destFamilies);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void CalculateConfidence_RFNMatch_Returns100Percent()
    {
        // Act
        var confidence = _service.CalculateConfidence(100, "RFN");

        // Assert
        Assert.Equal(1.0, confidence);
    }

    [Fact]
    public void CalculateConfidence_FuzzyMatch_ReturnsProportionalConfidence()
    {
        // Act
        var confidence = _service.CalculateConfidence(80, "Fuzzy");

        // Assert
        Assert.Equal(0.80, confidence, precision: 2);
    }

    [Fact]
    public void RollbackSuspiciousMappings_RemovesHighSeverityIssues()
    {
        // Arrange
        var mappings = new Dictionary<string, string>
        {
            { "@I1@", "@I100@" },
            { "@I2@", "@I101@" }
        };

        var validation = new ValidationResult
        {
            Issues = new[]
            {
                new MappingIssue
                {
                    SourceId = "@I1@",
                    DestId = "@I100@",
                    Type = IssueType.GenderMismatch,
                    Severity = IssueSeverity.High
                }
            }.ToImmutableList()
        };

        var sourceFamilies = new Dictionary<string, Family>();

        // Act
        var cleaned = _service.RollbackSuspiciousMappings(mappings, validation, sourceFamilies);

        // Assert
        Assert.Single(cleaned);
        Assert.False(cleaned.ContainsKey("@I1@"));
        Assert.True(cleaned.ContainsKey("@I2@"));
    }

    private PersonRecord CreatePerson(
        string id,
        Gender gender = Gender.Unknown,
        int? birthYear = null,
        int? deathYear = null)
    {
        return new PersonRecord
        {
            Id = id,
            Source = PersonSource.Gedcom,
            Gender = gender,
            BirthDate = birthYear.HasValue ? new DateInfo
            {
                Date = new DateOnly(birthYear.Value, 1, 1),
                Precision = DatePrecision.Year
            } : null,
            DeathDate = deathYear.HasValue ? new DateInfo
            {
                Date = new DateOnly(deathYear.Value, 1, 1),
                Precision = DatePrecision.Year
            } : null,
            FirstName = "Test",
            LastName = "Person"
        };
    }
}
