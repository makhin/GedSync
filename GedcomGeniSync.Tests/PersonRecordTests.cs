using System.Collections.Immutable;
using FluentAssertions;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Tests;

public class PersonRecordTests
{
    [Fact]
    public void BirthAndDeathYears_ShouldReflectDateInfo()
    {
        var record = new PersonRecord
        {
            Id = "1",
            Source = PersonSource.Gedcom,
            FirstName = "Ivan",
            LastName = "Petrov",
            BirthDate = new DateInfo { Date = new DateOnly(1980, 5, 6) },
            DeathDate = new DateInfo { Date = new DateOnly(2001, 1, 1) }
        };

        record.BirthYear.Should().Be(1980);
        record.DeathYear.Should().Be(2001);
    }

    [Fact]
    public void FullName_And_ToString_ShouldIncludeKeyFields()
    {
        var record = new PersonRecord
        {
            Id = "@I1@",
            Source = PersonSource.Geni,
            FirstName = "Ivan",
            MiddleName = "Ivanovich",
            LastName = "Petrov",
            BirthDate = new DateInfo { Date = new DateOnly(1980, 1, 1) },
            DeathDate = new DateInfo { Date = new DateOnly(2010, 1, 1) }
        };

        record.FullName.Should().Be("Ivan Ivanovich Petrov");
        record.ToString().Should().Contain("1980").And.Contain("2010");
    }

    [Fact]
    public void MatchCandidate_ToString_ShouldFormatScore()
    {
        var candidate = new MatchCandidate
        {
            Source = new PersonRecord { Id = "1", Source = PersonSource.Gedcom },
            Target = new PersonRecord { Id = "2", Source = PersonSource.Geni },
            Score = 88,
            Reasons = ImmutableList<MatchReason>.Empty
        };

        candidate.ToString().Should().Contain("88");
    }

    [Fact]
    public void OptionalFields_ShouldRoundTrip()
    {
        var record = new PersonRecord
        {
            Id = "1",
            Source = PersonSource.Gedcom,
            Suffix = "Jr.",
            Nickname = "Vanya",
            BurialDate = new DateInfo { Date = new DateOnly(2015, 5, 5) },
            BurialPlace = "Local Cemetery",
            DeathPlace = "Moscow",
            FatherId = "F1",
            MotherId = "M1",
            Occupation = "Engineer",
            MatchedId = "GENI123",
            MatchScore = 77,
            IsLiving = false
        };

        record.Suffix.Should().Be("Jr.");
        record.Nickname.Should().Be("Vanya");
        record.BurialPlace.Should().Be("Local Cemetery");
        record.DeathPlace.Should().Be("Moscow");
        record.FatherId.Should().Be("F1");
        record.MotherId.Should().Be("M1");
        record.Occupation.Should().Be("Engineer");
        record.MatchedId.Should().Be("GENI123");
        record.MatchScore.Should().Be(77);
        record.IsLiving.Should().BeFalse();
    }
}
