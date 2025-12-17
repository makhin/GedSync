using FluentAssertions;
using GedcomGeniSync.ApiClient.Models;

namespace GedcomGeniSync.Tests;

/// <summary>
/// Tests for Phase 1 changes: new GeniProfileUpdate fields and event models
/// </summary>
public class GeniProfileUpdatePhase1Tests
{
    [Fact]
    public void GeniDateInput_ShouldStoreAllComponents()
    {
        var date = new GeniDateInput
        {
            Year = 1974,
            Month = 12,
            Day = 17
        };

        date.Year.Should().Be(1974);
        date.Month.Should().Be(12);
        date.Day.Should().Be(17);
    }

    [Fact]
    public void GeniLocationInput_ShouldStorePlaceName()
    {
        var location = new GeniLocationInput
        {
            PlaceName = "Moscow, Russia"
        };

        location.PlaceName.Should().Be("Moscow, Russia");
    }

    [Fact]
    public void GeniEventInput_ShouldContainDateAndLocation()
    {
        var eventInput = new GeniEventInput
        {
            Date = new GeniDateInput { Year = 1950, Month = 3, Day = 15 },
            Location = new GeniLocationInput { PlaceName = "Moscow" }
        };

        eventInput.Date.Should().NotBeNull();
        eventInput.Date!.Year.Should().Be(1950);
        eventInput.Date.Month.Should().Be(3);
        eventInput.Date.Day.Should().Be(15);

        eventInput.Location.Should().NotBeNull();
        eventInput.Location!.PlaceName.Should().Be("Moscow");
    }

    [Fact]
    public void GeniProfileUpdate_ShouldSupportNewEventFields()
    {
        var update = new GeniProfileUpdate
        {
            Birth = new GeniEventInput
            {
                Date = new GeniDateInput { Year = 1950 },
                Location = new GeniLocationInput { PlaceName = "Moscow" }
            },
            Death = new GeniEventInput
            {
                Date = new GeniDateInput { Year = 2020 }
            },
            Baptism = new GeniEventInput
            {
                Date = new GeniDateInput { Year = 1950, Month = 4 }
            },
            Burial = new GeniEventInput
            {
                Location = new GeniLocationInput { PlaceName = "Cemetery" }
            }
        };

        update.Birth.Should().NotBeNull();
        update.Death.Should().NotBeNull();
        update.Baptism.Should().NotBeNull();
        update.Burial.Should().NotBeNull();
    }

    [Fact]
    public void GeniProfileUpdate_ShouldSupportMultilingualNames()
    {
        var update = new GeniProfileUpdate
        {
            Names = new Dictionary<string, Dictionary<string, string>>
            {
                ["ru"] = new Dictionary<string, string>
                {
                    ["first_name"] = "Иван",
                    ["last_name"] = "Иванов"
                },
                ["en"] = new Dictionary<string, string>
                {
                    ["first_name"] = "Ivan",
                    ["last_name"] = "Ivanov"
                }
            }
        };

        update.Names.Should().NotBeNull();
        update.Names.Should().ContainKey("ru");
        update.Names.Should().ContainKey("en");
        update.Names!["ru"]["first_name"].Should().Be("Иван");
        update.Names["en"]["first_name"].Should().Be("Ivan");
    }

    [Fact]
    public void GeniProfileUpdate_ShouldSupportNewSimpleFields()
    {
        var update = new GeniProfileUpdate
        {
            Nicknames = "Vanya,Johnny",
            Title = "Dr.",
            IsAlive = false,
            CauseOfDeath = "Natural causes"
        };

        update.Nicknames.Should().Be("Vanya,Johnny");
        update.Title.Should().Be("Dr.");
        update.IsAlive.Should().BeFalse();
        update.CauseOfDeath.Should().Be("Natural causes");
    }

    [Fact]
    public void GeniProfileUpdate_BackwardCompatibility_ShouldStillWorkWithOldFields()
    {
        // Ensure old code still works
        var update = new GeniProfileUpdate
        {
            FirstName = "John",
            LastName = "Doe",
            Gender = "male",
            Occupation = "Engineer",
            AboutMe = "Bio text"
        };

        update.FirstName.Should().Be("John");
        update.LastName.Should().Be("Doe");
        update.Gender.Should().Be("male");
        update.Occupation.Should().Be("Engineer");
        update.AboutMe.Should().Be("Bio text");
    }

    [Fact]
    public void GeniProfileUpdate_AllNewFields_CanBeNullable()
    {
        // All new fields should be optional
        var update = new GeniProfileUpdate
        {
            FirstName = "Test"
        };

        update.Birth.Should().BeNull();
        update.Death.Should().BeNull();
        update.Baptism.Should().BeNull();
        update.Burial.Should().BeNull();
        update.Names.Should().BeNull();
        update.Nicknames.Should().BeNull();
        update.Title.Should().BeNull();
        update.IsAlive.Should().BeNull();
        update.CauseOfDeath.Should().BeNull();
    }
}
