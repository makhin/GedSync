using System.Collections.Immutable;
using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GedcomGeniSync.Tests;

public class FuzzyMatcherServiceTests
{
    private readonly NameVariantsService _nameVariants = new(NullLogger<NameVariantsService>.Instance);

    private FuzzyMatcherService CreateService(MatchingOptions? options = null)
    {
        return new FuzzyMatcherService(_nameVariants, NullLogger<FuzzyMatcherService>.Instance, options);
    }

    [Fact]
    public void Compare_ShouldNormalizeScore_WhenWeightsNotSummingToOneHundred()
    {
        var options = new MatchingOptions
        {
            FirstNameWeight = 20,
            LastNameWeight = 20,
            BirthDateWeight = 20,
            BirthPlaceWeight = 10,
            DeathDateWeight = 5,
            GenderWeight = 5,
        };

        var person = CreatePerson(
            first: "Ivan",
            last: "Petrov",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1980, 1, 1) },
            birthPlace: "Moscow");

        var target = person with
        {
            NormalizedFirstName = "ivan",
            NormalizedLastName = "petrov",
            BirthDate = new DateInfo { Date = new DateOnly(1980, 1, 1) },
            BirthPlace = "Moscow, Russia"
        };

        var service = CreateService(options);

        var result = service.Compare(person, target);

        result.Score.Should().BeApproximately(81.25, precision: 0.01);
        result.Reasons.Should().HaveCount(4);
    }

    [Fact]
    public void Compare_ShouldApplyGenderPenalty_WhenGendersDiffer()
    {
        var person = CreatePerson(first: "Ivan", last: "Petrov", gender: Gender.Male);
        var target = CreatePerson(first: "Ivan", last: "Petrov", gender: Gender.Female);

        var service = CreateService();

        var result = service.Compare(person, target);

        result.Score.Should().BeLessThan(60);
        result.Reasons.Should().Contain(r => r.Field == "Gender" && r.Points < 0);
    }

    [Fact]
    public void FindMatches_ShouldFilterByGenderAndBirthYear()
    {
        var source = CreatePerson(
            first: "Ivan",
            last: "Petrov",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1980, 1, 1) });

        var farBirthYear = CreatePerson(
            first: "Ivan",
            last: "Petrov",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1950, 1, 1) });

        var wrongGender = CreatePerson(
            first: "Ivan",
            last: "Petrova",
            gender: Gender.Female,
            birthDate: new DateInfo { Date = new DateOnly(1980, 1, 1) });

        var closeMatch = CreatePerson(
            first: "Ivan",
            last: "Petrov",
            gender: Gender.Unknown,
            birthDate: new DateInfo { Date = new DateOnly(1981, 1, 1) });

        var service = CreateService();

        var results = service.FindMatches(source, new[] { farBirthYear, wrongGender, closeMatch }, minScore: 50);

        results.Should().ContainSingle();
        results[0].Target.Should().Be(closeMatch);
    }

    [Fact]
    public void Compare_ShouldUseNameVariants_ForEquivalentNames()
    {
        _nameVariants.AddGivenNameVariants("иван", new[] { "john" });

        var source = CreatePerson(first: "Иван", last: "Petrov");
        var target = CreatePerson(first: "John", last: "Petrov");

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Score.Should().BeGreaterThan(50);
        result.Reasons.Should().Contain(r => r.Field == "FirstName");
    }

    [Fact]
    public void Compare_ShouldHandleMaidenNameMatches()
    {
        var source = CreatePerson(first: "Anna", last: "Ivanova");
        var target = CreatePerson(first: "Anna", last: "Petrova", birthPlace: "Moscow") with
        {
            MaidenName = "Ivanova"
        };

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Reasons.Should().Contain(r => r.Field == "LastName" && r.Points >= 23);
    }

    [Fact]
    public void Compare_ShouldBoostSubstringNicknames()
    {
        var source = CreatePerson(first: "Alexander", last: "Petrov");
        var target = CreatePerson(first: "Alex", last: "Petrov");

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Reasons.Should().Contain(r => r.Field == "FirstName");
        result.Reasons.First(r => r.Field == "FirstName").Points.Should().BeGreaterThan(20);
    }

    [Fact]
    public void Compare_ShouldUsePlaceTokenSimilarity()
    {
        var source = CreatePerson(first: "Ivan", last: "Petrov", birthPlace: "Moscow USSR");
        var target = CreatePerson(first: "Ivan", last: "Petrov", birthPlace: "Moscow Russia");

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Reasons.Should().Contain(r => r.Field == "BirthPlace");
    }

    [Fact]
    public void Compare_ShouldDowngradeScoreForDistantBirthYears()
    {
        var source = CreatePerson(first: "Ivan", last: "Petrov", birthDate: new DateInfo { Date = new DateOnly(1900, 1, 1) });
        var target = CreatePerson(first: "Ivan", last: "Petrov", birthDate: new DateInfo { Date = new DateOnly(1906, 1, 1) });

        var service = CreateService(new MatchingOptions { MaxBirthYearDifference = 10 });

        var result = service.Compare(source, target);

        result.Reasons.Should().Contain(r => r.Field == "BirthDate");
        result.Score.Should().BeLessThan(90);
    }

    [Fact]
    public void Compare_ShouldReturnHighScore_ForExactMatch()
    {
        var source = CreatePerson(
            first: "Иван",
            last: "Петров",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1885, 5, 15) });
        var target = CreatePerson(
            first: "Иван",
            last: "Петров",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1885, 5, 15) });

        var service = CreateService();

        var result = service.Compare(source, target);

        // Score ~75% (firstName=30 + lastName=25 + birthDate=20)
        // Gender/birthPlace/deathDate only penalize mismatches, don't add points for matches
        result.Score.Should().BeGreaterThanOrEqualTo(70);
    }

    [Fact]
    public void Compare_ShouldReturnLowScore_ForCompletelyDifferentPersons()
    {
        var source = CreatePerson(
            first: "Иван",
            last: "Петров",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1885, 1, 1) });
        var target = CreatePerson(
            first: "Пётр",
            last: "Сидоров",
            gender: Gender.Male,
            birthDate: new DateInfo { Date = new DateOnly(1920, 1, 1) });

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Score.Should().BeLessThan(50);
    }

    [Fact]
    public void Compare_ShouldMatchCyrillicDiminutives_WhenVariantsConfigured()
    {
        _nameVariants.AddGivenNameVariants("александр", new[] { "саша", "шура", "алекс" });

        var source = CreatePerson(first: "Александр", last: "Смирнов");
        var target = CreatePerson(first: "Саша", last: "Смирнов");

        var service = CreateService();

        var result = service.Compare(source, target);

        result.Score.Should().BeGreaterThan(50);
        result.Reasons.Should().Contain(r => r.Field == "FirstName");
    }

    private static PersonRecord CreatePerson(
        string first,
        string last,
        Gender gender = Gender.Unknown,
        DateInfo? birthDate = null,
        string? birthPlace = null)
    {
        return new PersonRecord
        {
            Id = Guid.NewGuid().ToString(),
            Source = PersonSource.Gedcom,
            FirstName = first,
            LastName = last,
            Gender = gender,
            BirthDate = birthDate,
            BirthPlace = birthPlace,
            NameVariants = ImmutableList<string>.Empty
        };
    }
}
