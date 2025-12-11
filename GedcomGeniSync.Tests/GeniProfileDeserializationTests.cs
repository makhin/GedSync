using System.Text.Json;
using FluentAssertions;
using GedcomGeniSync.Services;

namespace GedcomGeniSync.Tests;

public class GeniProfileDeserializationTests
{
    [Fact]
    public void Deserialize_FamilyResponse_ShouldPopulateAllFields()
    {
        // Read the family-response.json file
        var jsonPath = Path.Combine("..", "..", "..", "..", "family-response.json");
        var json = File.ReadAllText(jsonPath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var batchResult = JsonSerializer.Deserialize<GeniBatchProfileResult>(json, options);

        batchResult.Should().NotBeNull();
        batchResult!.Results.Should().NotBeNull();
        batchResult.Results.Should().HaveCount(9);

        // Check first profile (Alexandr)
        var alexandr = batchResult.Results![0];
        alexandr.Id.Should().Be("profile-34828568625");
        alexandr.FirstName.Should().Be("Alexandr");
        alexandr.MiddleName.Should().Be("Владимирович");
        alexandr.MaidenName.Should().Be("Махин");
        alexandr.LastName.Should().Be("Makhin");
        alexandr.Gender.Should().Be("male");
        alexandr.IsAlive.Should().BeTrue();

        // Check Birth event
        alexandr.Birth.Should().NotBeNull();
        alexandr.Birth!.Date.Should().NotBeNull();
        alexandr.Birth.Date!.Day.Should().Be(17);
        alexandr.Birth.Date.Month.Should().Be(12);
        alexandr.Birth.Date.Year.Should().Be(1974);
        alexandr.Birth.Date.FormattedDate.Should().Be("December 17, 1974");

        alexandr.Birth.Location.Should().NotBeNull();
        alexandr.Birth.Location!.PlaceName.Should().Be("Днепропетровск");

        // Check computed property BirthDate
        alexandr.BirthDate.Should().Be("December 17, 1974");
        alexandr.BirthPlace.Should().Be("Днепропетровск");

        // Check multilingual names
        alexandr.Names.Should().NotBeNull();
        alexandr.Names.Should().ContainKey("en-US");
        alexandr.Names.Should().ContainKey("ru");

        alexandr.Names!["en-US"]["first_name"].Should().Be("Alexandr");
        alexandr.Names["en-US"]["last_name"].Should().Be("Makhin");

        alexandr.Names["ru"]["first_name"].Should().Be("Александр");
        alexandr.Names["ru"]["last_name"].Should().Be("Махин");

        // Check other fields
        alexandr.Occupation.Should().Be("Программист");
        alexandr.BigTree.Should().BeTrue();
        alexandr.Claimed.Should().BeTrue();
        alexandr.Language.Should().Be("ru");
        alexandr.Relationship.Should().Be("yourself");

        // Check second profile (Vladimir - father, with death)
        var vladimir = batchResult.Results.FirstOrDefault(p => p.FirstName == "Владимир");
        vladimir.Should().NotBeNull();
        vladimir!.IsAlive.Should().BeFalse();
        vladimir.Living.Should().BeFalse();

        vladimir.Death.Should().NotBeNull();
        vladimir.Death!.Date.Should().NotBeNull();
        vladimir.Death.Date!.Year.Should().Be(2004);
        vladimir.Death.Date.Month.Should().Be(12);
        vladimir.Death.Date.Day.Should().Be(15);

        vladimir.DeathDate.Should().Be("December 15, 2004");
        vladimir.DeathPlace.Should().Be("Днепропетровск");
    }
}
