using System.Text.Json;
using FluentAssertions;
using GedcomGeniSync.ApiClient.Models;

namespace GedcomGeniSync.Tests;

public class GeniUnionDeserializationTests
{
    [Fact]
    public void Deserialize_UnionsResponse_ShouldPopulateAllFields()
    {
        // Read the unions-response.json file
        var jsonPath = Path.Combine("..", "..", "..", "..", "unions-response.json");
        var json = File.ReadAllText(jsonPath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var batchResult = JsonSerializer.Deserialize<GeniBatchUnionResult>(json, options);

        batchResult.Should().NotBeNull();
        batchResult!.Results.Should().NotBeNull();
        batchResult.Results.Should().HaveCount(3);

        // Check first union (divorce case - union-118937407)
        var divorceUnion = batchResult.Results![0];
        divorceUnion.Id.Should().Be("union-118937407");
        divorceUnion.Guid.Should().Be("6000000207133980751");
        divorceUnion.Status.Should().Be("ex_spouse");
        divorceUnion.Url.Should().Be("https://www.geni.com/api/union-118937407");

        // Check divorce event
        divorceUnion.Divorce.Should().NotBeNull();
        divorceUnion.Divorce!.Name.Should().Be("Divorce of Alexandr Махин and Магдалина Зайцева");
        divorceUnion.Divorce.Date.Should().NotBeNull();
        divorceUnion.Divorce.Date!.Day.Should().Be(22);
        divorceUnion.Divorce.Date.Month.Should().Be(4);
        divorceUnion.Divorce.Date.Year.Should().Be(2013);
        divorceUnion.Divorce.Date.FormattedDate.Should().Be("April 22, 2013");
        divorceUnion.Divorce.Location.Should().NotBeNull();
        divorceUnion.Divorce.Location!.PlaceName.Should().Be("Днепропетровск");
        divorceUnion.Divorce.Location.FormattedLocation.Should().Be("Днепропетровск");

        // Check computed property DivorceDate
        divorceUnion.DivorceDate.Should().Be("April 22, 2013");
        divorceUnion.DivorcePlace.Should().Be("Днепропетровск");

        // Check partners and children
        divorceUnion.Partners.Should().NotBeNull();
        divorceUnion.Partners.Should().HaveCount(2);
        divorceUnion.Partners.Should().Contain("https://www.geni.com/api/profile-34828568625");
        divorceUnion.Partners.Should().Contain("https://www.geni.com/api/profile-34829663288");

        divorceUnion.Children.Should().NotBeNull();
        divorceUnion.Children.Should().HaveCount(2);
        divorceUnion.Children.Should().Contain("https://www.geni.com/api/profile-34829663289");
        divorceUnion.Children.Should().Contain("https://www.geni.com/api/profile-34829663292");

        // Check second union (marriage case - union-118937408)
        var marriageUnion = batchResult.Results[1];
        marriageUnion.Id.Should().Be("union-118937408");
        marriageUnion.Guid.Should().Be("6000000207133980755");
        marriageUnion.Status.Should().Be("spouse");

        // Check marriage event
        marriageUnion.Marriage.Should().NotBeNull();
        marriageUnion.Marriage!.Name.Should().Be("Marriage of Владимир Махин and Татьяна Рызванович");
        marriageUnion.Marriage.Date.Should().NotBeNull();
        marriageUnion.Marriage.Date!.Day.Should().Be(2);
        marriageUnion.Marriage.Date.Month.Should().Be(3);
        marriageUnion.Marriage.Date.Year.Should().Be(1973);
        marriageUnion.Marriage.Date.FormattedDate.Should().Be("March 2, 1973");
        marriageUnion.Marriage.Location.Should().NotBeNull();
        marriageUnion.Marriage.Location!.PlaceName.Should().Be("Днепропетровск");

        // Check computed property MarriageDate
        marriageUnion.MarriageDate.Should().Be("March 2, 1973");
        marriageUnion.MarriagePlace.Should().Be("Днепропетровск");

        // Also check legacy fields (marriage_date and marriage_location as objects)
        marriageUnion.MarriageDateObject.Should().NotBeNull();
        marriageUnion.MarriageDateObject!.FormattedDate.Should().Be("March 2, 1973");
        marriageUnion.MarriageLocationObject.Should().NotBeNull();
        marriageUnion.MarriageLocationObject!.PlaceName.Should().Be("Днепропетровск");

        // Check third union (another marriage - union-118937425)
        var thirdUnion = batchResult.Results[2];
        thirdUnion.Id.Should().Be("union-118937425");
        thirdUnion.Status.Should().Be("spouse");
        thirdUnion.MarriageDate.Should().Be("January 21, 2017");
        thirdUnion.MarriagePlace.Should().Be("Вроцлав");

        // Check NumericId helper
        divorceUnion.NumericId.Should().Be("118937407");
        marriageUnion.NumericId.Should().Be("118937408");
        thirdUnion.NumericId.Should().Be("118937425");
    }
}
