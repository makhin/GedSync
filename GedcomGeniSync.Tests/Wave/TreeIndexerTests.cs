using FluentAssertions;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Core.Services.Wave;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Patagames.GedcomNetSdk.Records.Ver551;

namespace GedcomGeniSync.Tests.Wave;

public class TreeIndexerTests
{
    private readonly TreeIndexer _indexer;

    public TreeIndexerTests()
    {
        _indexer = new TreeIndexer();
    }

    [Fact]
    public void BuildIndex_ShouldCreateEmptyGraph_WhenNoData()
    {
        // Arrange
        var loadResult = new GedcomLoadResult();

        // Act
        var graph = _indexer.BuildIndex(loadResult);

        // Assert
        graph.PersonsById.Should().BeEmpty();
        graph.FamiliesById.Should().BeEmpty();
        graph.PersonToFamiliesAsSpouse.Should().BeEmpty();
        graph.PersonToFamiliesAsChild.Should().BeEmpty();
    }

    [Fact]
    public void BuildIndex_ShouldIndexPersons()
    {
        // Arrange
        var loadResult = new GedcomLoadResult();
        loadResult.Persons["@I1@"] = CreatePerson("@I1@", "John", "Doe", 1980);
        loadResult.Persons["@I2@"] = CreatePerson("@I2@", "Jane", "Doe", 1982);

        // Act
        var graph = _indexer.BuildIndex(loadResult);

        // Assert
        graph.PersonsById.Should().HaveCount(2);
        graph.PersonsById["@I1@"].FirstName.Should().Be("John");
        graph.PersonsById["@I2@"].FirstName.Should().Be("Jane");
    }

    [Fact]
    public void BuildIndex_ShouldCreateBirthYearIndex()
    {
        // Arrange
        var loadResult = new GedcomLoadResult();
        loadResult.Persons["@I1@"] = CreatePerson("@I1@", "John", "Doe", 1980);
        loadResult.Persons["@I2@"] = CreatePerson("@I2@", "Jane", "Smith", 1980);
        loadResult.Persons["@I3@"] = CreatePerson("@I3@", "Bob", "Jones", 1990);

        // Act
        var graph = _indexer.BuildIndex(loadResult);

        // Assert
        graph.PersonsByBirthYear.Should().NotBeNull();
        graph.PersonsByBirthYear!.Should().HaveCount(2);
        graph.PersonsByBirthYear[1980].Should().HaveCount(2);
        graph.PersonsByBirthYear[1990].Should().HaveCount(1);
    }

    [Fact]
    public void BuildIndex_ShouldCreateLastNameIndex()
    {
        // Arrange
        var loadResult = new GedcomLoadResult();
        loadResult.Persons["@I1@"] = CreatePerson("@I1@", "John", "Doe", 1980, "doe");
        loadResult.Persons["@I2@"] = CreatePerson("@I2@", "Jane", "Doe", 1982, "doe");
        loadResult.Persons["@I3@"] = CreatePerson("@I3@", "Bob", "Smith", 1990, "smith");

        // Act
        var graph = _indexer.BuildIndex(loadResult);

        // Assert
        graph.PersonsByNormalizedLastName.Should().NotBeNull();
        graph.PersonsByNormalizedLastName!.Should().HaveCount(2);
        graph.PersonsByNormalizedLastName["doe"].Should().HaveCount(2);
        graph.PersonsByNormalizedLastName["smith"].Should().HaveCount(1);
    }

    // Helper methods

    private PersonRecord CreatePerson(string id, string firstName, string lastName, int? birthYear, string? normalizedLastName = null)
    {
        return new PersonRecord
        {
            Id = id,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName,
            BirthDate = birthYear.HasValue ? new DateInfo
            {
                Date = new DateOnly(birthYear.Value, 1, 1),
                Precision = DatePrecision.Year
            } : null,
            NormalizedLastName = normalizedLastName ?? lastName?.ToLowerInvariant()
        };
    }
}
