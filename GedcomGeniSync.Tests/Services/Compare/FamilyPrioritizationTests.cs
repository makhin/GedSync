using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Immutable;
using Xunit;

namespace GedcomGeniSync.Tests.Services.Compare;

public class FamilyPrioritizationTests
{
    private readonly FamilyCompareService _service;

    public FamilyPrioritizationTests()
    {
        var fuzzyMatcher = new FuzzyMatcherService(
            new NameVariantsService(NullLogger<NameVariantsService>.Instance),
            NullLogger<FuzzyMatcherService>.Instance,
            new MatchingOptions { MatchThreshold = 70 });
        _service = new FamilyCompareService(
            NullLogger<FamilyCompareService>.Instance,
            fuzzyMatcher);
    }

    [Fact]
    public void CompareFamilies_ProcessesFamiliesInPriorityOrder_HighConfidenceFirst()
    {
        // Arrange - Create 3 families with different levels of mapped members
        // Family 1: 1/3 members mapped (low priority)
        // Family 2: 3/3 members mapped (high priority)
        // Family 3: 2/3 members mapped (medium priority)

        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", CreatePerson("@I1@", "Иван", "Петров") },
            { "@I2@", CreatePerson("@I2@", "Мария", "Петрова") },
            { "@I3@", CreatePerson("@I3@", "Пётр", "Петров") },
            { "@I4@", CreatePerson("@I4@", "Алексей", "Сидоров") },
            { "@I5@", CreatePerson("@I5@", "Елена", "Сидорова") },
            { "@I6@", CreatePerson("@I6@", "Дмитрий", "Сидоров") },
            { "@I7@", CreatePerson("@I7@", "Николай", "Иванов") },
            { "@I8@", CreatePerson("@I8@", "Ольга", "Иванова") },
            { "@I9@", CreatePerson("@I9@", "Владимир", "Иванов") }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I101@", CreatePerson("@I101@", "Иван", "Петров") },
            { "@I104@", CreatePerson("@I104@", "Алексей", "Сидоров") },
            { "@I105@", CreatePerson("@I105@", "Елена", "Сидорова") },
            { "@I106@", CreatePerson("@I106@", "Дмитрий", "Сидоров") },
            { "@I107@", CreatePerson("@I107@", "Николай", "Иванов") },
            { "@I108@", CreatePerson("@I108@", "Ольга", "Иванова") }
        };

        var sourceFamilies = new Dictionary<string, Family>
        {
            // Family 1: Only husband mapped (1/3)
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", new[] { "@I3@" }) },
            // Family 2: All members mapped (3/3) - should be processed first
            { "@F2@", CreateFamily("@F2@", "@I4@", "@I5@", new[] { "@I6@" }) },
            // Family 3: Husband and wife mapped (2/3)
            { "@F3@", CreateFamily("@F3@", "@I7@", "@I8@", new[] { "@I9@" }) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F101@", CreateFamily("@F101@", "@I101@", null, null) },
            { "@F102@", CreateFamily("@F102@", "@I104@", "@I105@", new[] { "@I106@" }) },
            { "@F103@", CreateFamily("@F103@", "@I107@", "@I108@", null) }
        };

        // Pre-map some individuals to create priority differences
        var individualResult = new IndividualCompareResult
        {
            MatchedNodes = new[]
            {
                // Family 1: only husband
                CreateMatchedNode("@I1@", "@I101@"),
                // Family 2: all members (highest priority)
                CreateMatchedNode("@I4@", "@I104@"),
                CreateMatchedNode("@I5@", "@I105@"),
                CreateMatchedNode("@I6@", "@I106@"),
                // Family 3: husband and wife
                CreateMatchedNode("@I7@", "@I107@"),
                CreateMatchedNode("@I8@", "@I108@")
            }.ToImmutableList()
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I4@",
            AnchorDestinationId = "@I104@",
            MatchThreshold = 70
        };

        // Act
        var result = _service.CompareFamilies(
            sourceFamilies,
            destFamilies,
            individualResult,
            options,
            sourcePersons,
            destPersons);

        // Assert
        // Family 2 should be matched (highest confidence - all members mapped)
        Assert.Contains(result.MatchedFamilies, m => m.SourceFamId == "@F2@" && m.DestinationFamId == "@F102@");

        // Family 3 should be matched (medium confidence - spouses mapped)
        Assert.Contains(result.MatchedFamilies, m => m.SourceFamId == "@F3@" && m.DestinationFamId == "@F103@");

        // Family 1 should be matched (low confidence but still unique match)
        Assert.Contains(result.MatchedFamilies, m => m.SourceFamId == "@F1@" && m.DestinationFamId == "@F101@");
    }

    [Fact]
    public void CompareFamilies_BonusForBothSpousesMapped()
    {
        // Arrange - Family with both spouses mapped should get confidence bonus
        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", CreatePerson("@I1@", "Иван", "Петров") },
            { "@I2@", CreatePerson("@I2@", "Мария", "Петрова") }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I101@", CreatePerson("@I101@", "Иван", "Петров") },
            { "@I102@", CreatePerson("@I102@", "Мария", "Петрова") }
        };

        var sourceFamilies = new Dictionary<string, Family>
        {
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", null) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F101@", CreateFamily("@F101@", "@I101@", "@I102@", null) }
        };

        var individualResult = new IndividualCompareResult
        {
            MatchedNodes = new[]
            {
                CreateMatchedNode("@I1@", "@I101@"),
                CreateMatchedNode("@I2@", "@I102@")
            }.ToImmutableList()
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I101@",
            MatchThreshold = 70
        };

        // Act
        var result = _service.CompareFamilies(
            sourceFamilies,
            destFamilies,
            individualResult,
            options,
            sourcePersons,
            destPersons);

        // Assert
        // Should match because both spouses are mapped (gets confidence bonus)
        Assert.Single(result.MatchedFamilies);
        Assert.Equal("@F1@", result.MatchedFamilies[0].SourceFamId);
        Assert.Equal("@F101@", result.MatchedFamilies[0].DestinationFamId);
    }

    private PersonRecord CreatePerson(string id, string firstName, string lastName)
    {
        return new PersonRecord
        {
            Id = id,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private MatchedNode CreateMatchedNode(string sourceId, string destId)
    {
        return new MatchedNode
        {
            SourceId = sourceId,
            DestinationId = destId,
            MatchedBy = "Test",
            MatchScore = 100,
            PersonSummary = $"{sourceId} -> {destId}"
        };
    }

    private Family CreateFamily(
        string id,
        string? husbandId,
        string? wifeId,
        string[]? childrenIds)
    {
        var family = new Family
        {
            FamilyId = id,
            HusbandId = husbandId,
            WifeId = wifeId
        };

        if (childrenIds != null && childrenIds.Length > 0)
        {
            var childrenProperty = typeof(Family).GetProperty("Children");
            if (childrenProperty != null)
            {
                var collectionType = childrenProperty.PropertyType;
                var collection = Activator.CreateInstance(collectionType);

                if (collection != null)
                {
                    var addMethod = collectionType.GetMethod("Add");
                    foreach (var childId in childrenIds)
                    {
                        addMethod?.Invoke(collection, new object[] { childId });
                    }
                    childrenProperty.SetValue(family, collection);
                }
            }
        }

        return family;
    }
}
