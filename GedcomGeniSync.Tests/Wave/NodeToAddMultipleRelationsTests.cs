using System.Collections.Immutable;
using GedcomGeniSync.Models;
using Xunit;

namespace GedcomGeniSync.Tests.Wave;

/// <summary>
/// Tests for NodeToAdd model extensions to support multiple relations
/// </summary>
public class NodeToAddMultipleRelationsTests
{
    [Fact]
    public void NodeToAdd_WithAdditionalRelations_ShouldStoreMultipleRelations()
    {
        // Arrange & Act
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I1@",
            PersonData = new PersonData
            {
                FirstName = "John",
                LastName = "Doe"
            },
            RelatedToNodeId = "@I2@", // Primary relation (father)
            RelationType = CompareRelationType.Child,
            AdditionalRelations = ImmutableList.Create(
                new AdditionalRelation
                {
                    RelatedToNodeId = "@I3@", // Mother
                    RelationType = CompareRelationType.Child
                }
            ),
            SourceFamilyId = "@F1@",
            DepthFromExisting = 1
        };

        // Assert
        Assert.Equal("@I1@", nodeToAdd.SourceId);
        Assert.Equal("@I2@", nodeToAdd.RelatedToNodeId);
        Assert.Equal(CompareRelationType.Child, nodeToAdd.RelationType);

        // Check additional relations
        Assert.Single(nodeToAdd.AdditionalRelations);
        Assert.Equal("@I3@", nodeToAdd.AdditionalRelations[0].RelatedToNodeId);
        Assert.Equal(CompareRelationType.Child, nodeToAdd.AdditionalRelations[0].RelationType);

        // Check source family ID
        Assert.Equal("@F1@", nodeToAdd.SourceFamilyId);
    }

    [Fact]
    public void NodeToAdd_WithMultipleSpouses_ShouldStoreAllSpouses()
    {
        // Arrange & Act: Person with multiple spouses
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I1@",
            PersonData = new PersonData
            {
                FirstName = "Jane",
                LastName = "Smith"
            },
            RelatedToNodeId = "@I2@", // First spouse
            RelationType = CompareRelationType.Spouse,
            AdditionalRelations = ImmutableList.Create(
                new AdditionalRelation
                {
                    RelatedToNodeId = "@I3@", // Second spouse
                    RelationType = CompareRelationType.Spouse
                },
                new AdditionalRelation
                {
                    RelatedToNodeId = "@I4@", // Third spouse
                    RelationType = CompareRelationType.Spouse
                }
            ),
            DepthFromExisting = 1
        };

        // Assert
        Assert.Equal("@I2@", nodeToAdd.RelatedToNodeId);
        Assert.Equal(CompareRelationType.Spouse, nodeToAdd.RelationType);

        Assert.Equal(2, nodeToAdd.AdditionalRelations.Count);
        Assert.Equal("@I3@", nodeToAdd.AdditionalRelations[0].RelatedToNodeId);
        Assert.Equal("@I4@", nodeToAdd.AdditionalRelations[1].RelatedToNodeId);
        Assert.All(nodeToAdd.AdditionalRelations, r =>
            Assert.Equal(CompareRelationType.Spouse, r.RelationType));
    }

    [Fact]
    public void NodeToAdd_WithoutAdditionalRelations_ShouldHaveEmptyList()
    {
        // Arrange & Act: Node with single relation (backward compatibility)
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I1@",
            PersonData = new PersonData
            {
                FirstName = "Bob",
                LastName = "Johnson"
            },
            RelatedToNodeId = "@I2@",
            RelationType = CompareRelationType.Child,
            DepthFromExisting = 1
        };

        // Assert
        Assert.Empty(nodeToAdd.AdditionalRelations);
        Assert.Null(nodeToAdd.SourceFamilyId);
    }

    [Fact]
    public void AdditionalRelation_ShouldRequireAllFields()
    {
        // Arrange & Act
        var relation = new AdditionalRelation
        {
            RelatedToNodeId = "@I1@",
            RelationType = CompareRelationType.Parent
        };

        // Assert
        Assert.Equal("@I1@", relation.RelatedToNodeId);
        Assert.Equal(CompareRelationType.Parent, relation.RelationType);
    }

    [Fact]
    public void NodeToAdd_WithBothParents_ShouldHaveCorrectStructure()
    {
        // Arrange & Act: Child with both parents (the main use case for Phase 1)
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I5@",
            PersonData = new PersonData
            {
                FirstName = "Child",
                LastName = "Person",
                BirthDate = "2000-01-01"
            },
            RelatedToNodeId = "@I1@", // Father (primary)
            RelationType = CompareRelationType.Child,
            AdditionalRelations = ImmutableList.Create(
                new AdditionalRelation
                {
                    RelatedToNodeId = "@I2@", // Mother (additional)
                    RelationType = CompareRelationType.Child
                }
            ),
            SourceFamilyId = "@F1@", // Family where both parents are spouses
            DepthFromExisting = 1
        };

        // Assert - this is the key scenario we're fixing
        Assert.Equal("@I1@", nodeToAdd.RelatedToNodeId); // Father
        Assert.Equal(CompareRelationType.Child, nodeToAdd.RelationType);

        Assert.Single(nodeToAdd.AdditionalRelations);
        Assert.Equal("@I2@", nodeToAdd.AdditionalRelations[0].RelatedToNodeId); // Mother
        Assert.Equal(CompareRelationType.Child, nodeToAdd.AdditionalRelations[0].RelationType);

        Assert.Equal("@F1@", nodeToAdd.SourceFamilyId); // Can be used to find union
    }

    [Fact]
    public void NodeToAdd_Serialization_ShouldIncludeNewFields()
    {
        // Arrange
        var nodeToAdd = new NodeToAdd
        {
            SourceId = "@I1@",
            PersonData = new PersonData { FirstName = "Test", LastName = "Person" },
            RelatedToNodeId = "@I2@",
            RelationType = CompareRelationType.Child,
            AdditionalRelations = ImmutableList.Create(
                new AdditionalRelation
                {
                    RelatedToNodeId = "@I3@",
                    RelationType = CompareRelationType.Child
                }
            ),
            SourceFamilyId = "@F1@",
            DepthFromExisting = 1
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(nodeToAdd, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert - verify JSON contains new fields
        Assert.Contains("additionalRelations", json);
        Assert.Contains("sourceFamilyId", json);
        Assert.Contains("@F1@", json);
        Assert.Contains("@I3@", json);
    }
}
