using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using Xunit;

namespace GedcomGeniSync.Tests;

public class PersonFieldComparerTests
{
    private readonly PersonFieldComparer _comparer;

    public PersonFieldComparerTests()
    {
        _comparer = new PersonFieldComparer(NullLogger<PersonFieldComparer>.Instance);
    }

    [Fact]
    public void CompareFields_IdenticalRecords_ReturnsNoDifferences()
    {
        // Arrange
        var source = CreateTestPerson("John", "Doe", "1980");
        var destination = CreateTestPerson("John", "Doe", "1980");

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Empty(differences);
    }

    [Fact]
    public void CompareFields_SourceHasBirthPlace_DestinationEmpty_ReturnsDifference()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            BirthPlace = "New York"
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            BirthPlace = null
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        var diff = Assert.Single(differences);
        Assert.Equal("BirthPlace", diff.FieldName);
        Assert.Equal("New York", diff.SourceValue);
        Assert.Null(diff.DestinationValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_SourceHasFullDate_DestinationHasYear_ReturnsUpdate()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            BirthDate = DateInfo.Parse("15 MAR 1950") // Day precision
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            BirthDate = DateInfo.Parse("1950") // Year precision
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        var diff = Assert.Single(differences);
        Assert.Equal("BirthDate", diff.FieldName);
        Assert.Equal(FieldAction.Update, diff.Action);
        Assert.NotNull(diff.SourceValue);
        Assert.NotNull(diff.DestinationValue);
    }

    [Fact]
    public void CompareFields_SourceHasPhoto_DestinationEmpty_ReturnsAddPhoto()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            PhotoUrls = ImmutableList.Create("https://example.com/photo.jpg")
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            PhotoUrls = ImmutableList<string>.Empty
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        var diff = Assert.Single(differences);
        Assert.Equal("PhotoUrl", diff.FieldName);
        Assert.Equal("https://example.com/photo.jpg", diff.SourceValue);
        Assert.Equal(FieldAction.AddPhoto, diff.Action);
    }

    [Fact]
    public void CompareFields_MultipleFieldDifferences_ReturnsAll()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            LastName = "Doe",
            BirthPlace = "New York",
            DeathPlace = "Boston",
            BirthDate = DateInfo.Parse("1950")
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            LastName = "Doe",
            BirthPlace = null,      // Missing
            DeathPlace = null,      // Missing
            BirthDate = null        // Missing
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Equal(3, differences.Count);
        Assert.Contains(differences, d => d.FieldName == "BirthPlace");
        Assert.Contains(differences, d => d.FieldName == "DeathPlace");
        Assert.Contains(differences, d => d.FieldName == "BirthDate");
    }

    [Fact]
    public void CompareFields_SourceEmpty_DestinationHasValue_NoDifference()
    {
        // Arrange - we don't remove data from destination
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            BirthPlace = null
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            BirthPlace = "New York"
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Empty(differences);
    }

    [Fact]
    public void CompareFields_GenderSourceKnown_DestinationUnknown_ReturnsDifference()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            Gender = Gender.Male
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            Gender = Gender.Unknown
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        var diff = Assert.Single(differences);
        Assert.Equal("Gender", diff.FieldName);
        Assert.Equal("Male", diff.SourceValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_MaidenNameDifference_Detected()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            LastName = "Doe",
            MaidenName = "Smith"
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            LastName = "Doe",
            MaidenName = null
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        var diff = Assert.Single(differences);
        Assert.Equal("MaidenName", diff.FieldName);
        Assert.Equal("Smith", diff.SourceValue);
    }

    [Fact]
    public void CompareFields_SamePrecisionDates_NoDifference()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            BirthDate = DateInfo.Parse("1950") // Year precision
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            BirthDate = DateInfo.Parse("1950") // Year precision
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Empty(differences);
    }

    [Fact]
    public void AreFieldsIdentical_IdenticalRecords_ReturnsTrue()
    {
        // Arrange
        var source = CreateTestPerson("John", "Doe", "1980");
        var destination = CreateTestPerson("John", "Doe", "1980");

        // Act
        var result = _comparer.AreFieldsIdentical(source, destination);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreFieldsIdentical_DifferentRecords_ReturnsFalse()
    {
        // Arrange
        var source = CreateTestPerson("John", "Doe", "1980");
        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            LastName = "Doe",
            BirthPlace = null // Different - source has birth place
        };

        // Act
        var result = _comparer.AreFieldsIdentical(source, destination);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CompareFields_NicknameAndSuffix_Detected()
    {
        // Arrange
        var source = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            LastName = "Doe",
            Nickname = "Johnny",
            Suffix = "Jr."
        };

        var destination = new PersonRecord
        {
            Id = "@I2@",
            Source = PersonSource.Gedcom,
            FirstName = "John",
            LastName = "Doe",
            Nickname = null,
            Suffix = null
        };

        // Act
        var differences = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.FieldName == "Nickname" && d.SourceValue == "Johnny");
        Assert.Contains(differences, d => d.FieldName == "Suffix" && d.SourceValue == "Jr.");
    }

    private PersonRecord CreateTestPerson(string firstName, string lastName, string birthYear)
    {
        return new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName,
            BirthDate = DateInfo.Parse(birthYear),
            BirthPlace = "Test City"
        };
    }
}
