using System.Collections.Immutable;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;

namespace GedcomGeniSync.Tests.Services.Compare;

public class PersonFieldComparerTests
{
    private readonly PersonFieldComparer _comparer;

    public PersonFieldComparerTests()
    {
        _comparer = new PersonFieldComparer(NullLogger<PersonFieldComparer>.Instance);
    }

    [Fact]
    public void CompareFields_BothPersonsIdentical_ReturnsEmptyList()
    {
        // Arrange
        var source = CreatePerson(
            firstName: "John",
            lastName: "Doe",
            birthYear: 1980,
            birthPlace: "New York");

        var destination = CreatePerson(
            firstName: "John",
            lastName: "Doe",
            birthYear: 1980,
            birthPlace: "New York");

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Empty(diffs);
    }

    [Fact]
    public void CompareFields_DestinationMissingBirthPlace_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(birthPlace: "Moscow");
        var destination = CreatePerson(birthPlace: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("BirthPlace", diff.FieldName);
        Assert.Equal("Moscow", diff.SourceValue);
        Assert.Null(diff.DestinationValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_DestinationMissingMultipleFields_ReturnsMultipleDiffs()
    {
        // Arrange
        var source = CreatePerson(
            firstName: "Ivan",
            middleName: "Petrovich",
            birthPlace: "Moscow",
            deathPlace: "St. Petersburg");

        var destination = CreatePerson(
            firstName: "Ivan",
            middleName: null,
            birthPlace: null,
            deathPlace: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Equal(3, diffs.Count);
        Assert.Contains(diffs, d => d.FieldName == "MiddleName" && d.Action == FieldAction.Add);
        Assert.Contains(diffs, d => d.FieldName == "BirthPlace" && d.Action == FieldAction.Add);
        Assert.Contains(diffs, d => d.FieldName == "DeathPlace" && d.Action == FieldAction.Add);
    }

    [Fact]
    public void CompareFields_SourceHasFullDateDestinationHasYear_ReturnsUpdateDiff()
    {
        // Arrange
        var source = CreatePerson(birthDate: new DateInfo
        {
            Date = new DateOnly(1950, 3, 15),
            Precision = DatePrecision.Day,
            Original = "15 MAR 1950"
        });

        var destination = CreatePerson(birthDate: new DateInfo
        {
            Date = new DateOnly(1950, 1, 1),
            Precision = DatePrecision.Year,
            Original = "1950"
        });

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("BirthDate", diff.FieldName);
        Assert.Equal("15 MAR 1950", diff.SourceValue);
        Assert.Equal("1950", diff.DestinationValue);
        Assert.Equal(FieldAction.Update, diff.Action);
    }

    [Fact]
    public void CompareFields_DestinationMissingPhotoUrl_ReturnsAddPhotoDiff()
    {
        // Arrange
        var source = CreatePerson(photoUrls: new[] { "https://example.com/photo.jpg" });
        var destination = CreatePerson(photoUrls: Array.Empty<string>());

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("PhotoUrl", diff.FieldName);
        Assert.Equal("https://example.com/photo.jpg", diff.SourceValue);
        Assert.Null(diff.DestinationValue);
        Assert.Equal(FieldAction.AddPhoto, diff.Action);
    }

    [Fact]
    public void CompareFields_BothHaveSamePhotoUrl_ReturnsEmptyList()
    {
        // Arrange
        var photoUrl = "https://example.com/photo.jpg";
        var source = CreatePerson(photoUrls: new[] { photoUrl });
        var destination = CreatePerson(photoUrls: new[] { photoUrl });

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Empty(diffs);
    }

    [Fact]
    public void CompareFields_SourceHasMultiplePhotosDestinationHasOne_ReturnsAddPhotoDiff()
    {
        // Arrange
        var source = CreatePerson(photoUrls: new[]
        {
            "https://example.com/photo1.jpg",
            "https://example.com/photo2.jpg",
            "https://example.com/photo3.jpg"
        });

        var destination = CreatePerson(photoUrls: new[] { "https://example.com/photo1.jpg" });

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        // Current implementation returns only the first new photo
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("PhotoUrl", diff.FieldName);
        Assert.Equal(FieldAction.AddPhoto, diff.Action);
        // Should be one of the new photos (photo2 or photo3)
        Assert.Contains(diff.SourceValue, new[] { "https://example.com/photo2.jpg", "https://example.com/photo3.jpg" });
    }

    [Fact]
    public void CompareFields_SourceNullDestinationHasValue_ReturnsEmptyList()
    {
        // Arrange
        var source = CreatePerson(birthPlace: null);
        var destination = CreatePerson(birthPlace: "Moscow");

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        // We only suggest adding data from source to dest, not removing
        Assert.Empty(diffs);
    }

    [Fact]
    public void CompareFields_MaidenNameMissing_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(maidenName: "Smith");
        var destination = CreatePerson(maidenName: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("MaidenName", diff.FieldName);
        Assert.Equal("Smith", diff.SourceValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_NicknameMissing_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(nickname: "Johnny");
        var destination = CreatePerson(nickname: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("Nickname", diff.FieldName);
        Assert.Equal("Johnny", diff.SourceValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_SuffixMissing_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(suffix: "Jr.");
        var destination = CreatePerson(suffix: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("Suffix", diff.FieldName);
        Assert.Equal("Jr.", diff.SourceValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_DeathDateMissing_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(deathDate: new DateInfo
        {
            Date = new DateOnly(2020, 5, 10),
            Precision = DatePrecision.Day,
            Original = "10 MAY 2020"
        });

        var destination = CreatePerson(deathDate: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("DeathDate", diff.FieldName);
        Assert.Equal("10 MAY 2020", diff.SourceValue);
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    [Fact]
    public void CompareFields_BurialDateAndPlaceMissing_ReturnsTwoDiffs()
    {
        // Arrange
        var source = CreatePerson(
            burialDate: new DateInfo
            {
                Date = new DateOnly(2020, 5, 15),
                Precision = DatePrecision.Day,
                Original = "15 MAY 2020"
            },
            burialPlace: "Novodevichy Cemetery");

        var destination = CreatePerson(burialDate: null, burialPlace: null);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Equal(2, diffs.Count);
        Assert.Contains(diffs, d => d.FieldName == "BurialDate" && d.SourceValue == "15 MAY 2020");
        Assert.Contains(diffs, d => d.FieldName == "BurialPlace" && d.SourceValue == "Novodevichy Cemetery");
    }

    [Fact]
    public void CompareFields_GenderMissing_ReturnsAddDiff()
    {
        // Arrange
        var source = CreatePerson(gender: Gender.Male);
        var destination = CreatePerson(gender: Gender.Unknown);

        // Act
        var diffs = _comparer.CompareFields(source, destination);

        // Assert
        Assert.Single(diffs);
        var diff = diffs[0];
        Assert.Equal("Gender", diff.FieldName);
        Assert.Equal("Male", diff.SourceValue);
        Assert.Null(diff.DestinationValue); // Destination gender is Unknown, represented as null in diff
        Assert.Equal(FieldAction.Add, diff.Action);
    }

    private PersonRecord CreatePerson(
        string? firstName = null,
        string? middleName = null,
        string? lastName = null,
        string? maidenName = null,
        string? nickname = null,
        string? suffix = null,
        int? birthYear = null,
        DateInfo? birthDate = null,
        string? birthPlace = null,
        DateInfo? deathDate = null,
        string? deathPlace = null,
        DateInfo? burialDate = null,
        string? burialPlace = null,
        Gender gender = Gender.Unknown,
        string[]? photoUrls = null)
    {
        return new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            MiddleName = middleName,
            LastName = lastName,
            MaidenName = maidenName,
            Nickname = nickname,
            Suffix = suffix,
            Gender = gender,
            BirthDate = birthDate ?? (birthYear.HasValue
                ? new DateInfo
                {
                    Date = new DateOnly(birthYear.Value, 1, 1),
                    Precision = DatePrecision.Year,
                    Original = birthYear.Value.ToString()
                }
                : null),
            BirthPlace = birthPlace,
            DeathDate = deathDate,
            DeathPlace = deathPlace,
            BurialDate = burialDate,
            BurialPlace = burialPlace,
            PhotoUrls = photoUrls?.ToImmutableList() ?? ImmutableList<string>.Empty
        };
    }
}
