using GedcomGeniSync.Models;
using Xunit;

namespace GedcomGeniSync.Tests;

public class PersonRecordGeniIdTests
{
    [Fact]
    public void GetNumericGeniId_WithRfnTag_ReturnsNumericId()
    {
        // Arrange
        var person = new PersonRecord
        {
            Id = "@I6000000206529622827@",
            Source = PersonSource.Gedcom,
            GeniProfileId = "geni:6000000206529622827"
        };

        // Act
        var numericId = person.GetNumericGeniId();

        // Assert
        Assert.Equal("6000000206529622827", numericId);
    }

    [Fact]
    public void GetNumericGeniId_WithoutRfnTag_ExtractsFromGedcomId()
    {
        // Arrange
        var person = new PersonRecord
        {
            Id = "@I6000000206529622827@",
            Source = PersonSource.Gedcom,
            GeniProfileId = null
        };

        // Act
        var numericId = person.GetNumericGeniId();

        // Assert
        Assert.Equal("6000000206529622827", numericId);
    }

    [Fact]
    public void GetNumericGeniId_WithProfileFormat_ReturnsNumericId()
    {
        // Arrange
        var person = new PersonRecord
        {
            Id = "@I123@",
            Source = PersonSource.Gedcom,
            GeniProfileId = "profile-6000000206529622827"
        };

        // Act
        var numericId = person.GetNumericGeniId();

        // Assert
        Assert.Equal("6000000206529622827", numericId);
    }

    [Fact]
    public void GetNumericGeniId_InvalidFormat_ReturnsNull()
    {
        // Arrange
        var person = new PersonRecord
        {
            Id = "INVALID-ID", // Invalid format, cannot extract numeric ID
            Source = PersonSource.Gedcom,
            GeniProfileId = null
        };

        // Act
        var numericId = person.GetNumericGeniId();

        // Assert
        Assert.Null(numericId);
    }

    [Fact]
    public void GetNumericGeniId_RfnTakesPrecedenceOverGedcomId()
    {
        // Arrange - RFN and GEDCOM ID have different numeric IDs
        var person = new PersonRecord
        {
            Id = "@I1111111111111111111@",
            Source = PersonSource.Gedcom,
            GeniProfileId = "geni:6000000206529622827"
        };

        // Act
        var numericId = person.GetNumericGeniId();

        // Assert - Should return numeric ID from RFN, not from GEDCOM ID
        Assert.Equal("6000000206529622827", numericId);
    }

    [Fact]
    public void TwoPersons_SameGeniId_MatchByNumericId()
    {
        // Arrange
        var personFromSource = new PersonRecord
        {
            Id = "@I100@",
            Source = PersonSource.Gedcom,
            GeniProfileId = null // MyHeritage export without RFN
        };

        var personFromDestination = new PersonRecord
        {
            Id = "@I6000000206529622827@",
            Source = PersonSource.Gedcom,
            GeniProfileId = "geni:6000000206529622827" // Geni export with RFN
        };

        // This simulates when source person has GEDCOM ID matching the numeric part
        personFromSource = personFromSource with { Id = "@I6000000206529622827@" };

        // Act
        var numericId1 = personFromSource.GetNumericGeniId();
        var numericId2 = personFromDestination.GetNumericGeniId();

        // Assert
        Assert.Equal(numericId1, numericId2);
        Assert.Equal("6000000206529622827", numericId1);
    }
}
