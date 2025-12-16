using System.Collections.Immutable;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Tests.Wave;

/// <summary>
/// Tests for WaveHighConfidenceReport model to ensure it produces correct JSON structure
/// compatible with the compare command output.
/// </summary>
public class WaveHighConfidenceReportTests
{
    [Fact]
    public void WaveHighConfidenceReport_ShouldHaveCorrectStructure()
    {
        // Arrange
        var anchors = new AnchorInfo
        {
            SourceId = "@I1@",
            DestinationId = "@I2@",
            SourcePersonSummary = "John Doe (1950-2020)",
            DestinationPersonSummary = "John Doe (1950-2020)"
        };

        var options = new WaveCompareOptions
        {
            MaxLevel = 10,
            ThresholdStrategy = ThresholdStrategy.Adaptive,
            BaseThreshold = 60
        };

        var nodesToUpdate = ImmutableList.Create(
            new NodeToUpdate
            {
                SourceId = "@I3@",
                DestinationId = "@I4@",
                GeniProfileId = "profile-123",
                MatchScore = 95,
                MatchedBy = "Spouse",
                PersonSummary = "Jane Doe (1955-)",
                FieldsToUpdate = ImmutableList.Create(
                    new FieldDiff
                    {
                        FieldName = "BirthPlace",
                        SourceValue = "New York",
                        DestinationValue = "NY",
                        Action = FieldAction.Update
                    }
                )
            }
        );

        var nodesToAdd = ImmutableList.Create(
            new NodeToAdd
            {
                SourceId = "@I5@",
                PersonData = new PersonData
                {
                    FirstName = "Bob",
                    LastName = "Doe",
                    BirthDate = "1980-01-01",
                    Gender = "M"
                },
                RelatedToNodeId = "@I3@",
                RelationType = CompareRelationType.Child,
                DepthFromExisting = 1
            }
        );

        var individuals = new WaveIndividualsReport
        {
            NodesToUpdate = nodesToUpdate,
            NodesToAdd = nodesToAdd
        };

        // Act
        var report = new WaveHighConfidenceReport
        {
            SourceFile = "source.ged",
            DestinationFile = "dest.ged",
            Anchors = anchors,
            Options = options,
            Individuals = individuals
        };

        // Assert
        Assert.NotNull(report);
        Assert.Equal("source.ged", report.SourceFile);
        Assert.Equal("dest.ged", report.DestinationFile);
        Assert.NotNull(report.Anchors);
        Assert.NotNull(report.Options);
        Assert.NotNull(report.Individuals);
        Assert.Single(report.Individuals.NodesToUpdate);
        Assert.Single(report.Individuals.NodesToAdd);
    }

    [Fact]
    public void WaveHighConfidenceReport_NodesToUpdate_ShouldContainRequiredFields()
    {
        // Arrange
        var nodeToUpdate = new NodeToUpdate
        {
            SourceId = "@I1@",
            DestinationId = "@I2@",
            GeniProfileId = "profile-456",
            MatchScore = 92,
            MatchedBy = "Parent",
            PersonSummary = "Test Person (1960-2010)",
            FieldsToUpdate = ImmutableList.Create(
                new FieldDiff
                {
                    FieldName = "DeathPlace",
                    SourceValue = "Moscow",
                    DestinationValue = null,
                    Action = FieldAction.Add
                }
            )
        };

        var report = CreateMinimalReport(
            nodesToUpdate: ImmutableList.Create(nodeToUpdate),
            nodesToAdd: ImmutableList<NodeToAdd>.Empty
        );

        // Assert
        var update = report.Individuals.NodesToUpdate.First();
        Assert.Equal("@I1@", update.SourceId);
        Assert.Equal("@I2@", update.DestinationId);
        Assert.Equal("profile-456", update.GeniProfileId);
        Assert.Equal(92, update.MatchScore);
        Assert.Equal("Parent", update.MatchedBy);
        Assert.Equal("Test Person (1960-2010)", update.PersonSummary);
        Assert.Single(update.FieldsToUpdate);
        Assert.Equal("DeathPlace", update.FieldsToUpdate.First().FieldName);
        Assert.Equal("Moscow", update.FieldsToUpdate.First().SourceValue);
        Assert.Equal(FieldAction.Add, update.FieldsToUpdate.First().Action);
    }

    [Fact]
    public void WaveHighConfidenceReport_NodesToAdd_ShouldContainRequiredFields()
    {
        // Arrange
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I10@",
            PersonData = new PersonData
            {
                FirstName = "Alice",
                LastName = "Smith",
                BirthDate = "1990-05-15",
                BirthPlace = "London",
                Gender = "F"
            },
            RelatedToNodeId = "@I5@",
            RelationType = CompareRelationType.Spouse,
            DepthFromExisting = 2
        };

        var report = CreateMinimalReport(
            nodesToUpdate: ImmutableList<NodeToUpdate>.Empty,
            nodesToAdd: ImmutableList.Create(nodeToAdd)
        );

        // Assert
        var add = report.Individuals.NodesToAdd.First();
        Assert.Equal("@I10@", add.SourceId);
        Assert.NotNull(add.PersonData);
        Assert.Equal("Alice", add.PersonData.FirstName);
        Assert.Equal("Smith", add.PersonData.LastName);
        Assert.Equal("1990-05-15", add.PersonData.BirthDate);
        Assert.Equal("London", add.PersonData.BirthPlace);
        Assert.Equal("F", add.PersonData.Gender);
        Assert.Equal("@I5@", add.RelatedToNodeId);
        Assert.Equal(CompareRelationType.Spouse, add.RelationType);
        Assert.Equal(2, add.DepthFromExisting);
    }

    [Fact]
    public void WaveHighConfidenceReport_Serialization_ShouldProduceValidJson()
    {
        // Arrange
        var report = CreateMinimalReport(
            nodesToUpdate: ImmutableList.Create(
                new NodeToUpdate
                {
                    SourceId = "@I1@",
                    DestinationId = "@I2@",
                    MatchScore = 95,
                    MatchedBy = "Fuzzy",
                    PersonSummary = "Test (1950-)",
                    FieldsToUpdate = ImmutableList<FieldDiff>.Empty
                }
            ),
            nodesToAdd: ImmutableList.Create(
                new NodeToAdd
                {
                    SourceId = "@I3@",
                    PersonData = new PersonData { FirstName = "New", LastName = "Person" },
                    DepthFromExisting = 1
                }
            )
        );

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"sourceFile\"", json);
        Assert.Contains("\"destinationFile\"", json);
        Assert.Contains("\"anchors\"", json);
        Assert.Contains("\"options\"", json);
        Assert.Contains("\"individuals\"", json);
        Assert.Contains("\"nodesToUpdate\"", json);
        Assert.Contains("\"nodesToAdd\"", json);
        Assert.Contains("\"@I1@\"", json);
        Assert.Contains("\"@I2@\"", json);
        Assert.Contains("\"@I3@\"", json);
    }

    private static WaveHighConfidenceReport CreateMinimalReport(
        ImmutableList<NodeToUpdate> nodesToUpdate,
        ImmutableList<NodeToAdd> nodesToAdd)
    {
        return new WaveHighConfidenceReport
        {
            SourceFile = "test-source.ged",
            DestinationFile = "test-dest.ged",
            Anchors = new AnchorInfo
            {
                SourceId = "@I1@",
                DestinationId = "@I1@"
            },
            Options = new WaveCompareOptions
            {
                MaxLevel = 5,
                ThresholdStrategy = ThresholdStrategy.Adaptive,
                BaseThreshold = 60
            },
            Individuals = new WaveIndividualsReport
            {
                NodesToUpdate = nodesToUpdate,
                NodesToAdd = nodesToAdd
            }
        };
    }
}
