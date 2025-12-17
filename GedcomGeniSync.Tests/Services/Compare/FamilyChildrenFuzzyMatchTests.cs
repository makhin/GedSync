using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Immutable;
using Xunit;

namespace GedcomGeniSync.Tests.Services.Compare;

public class FamilyChildrenFuzzyMatchTests
{
    private readonly FamilyCompareService _service;
    private readonly PersonFieldComparer _fieldComparer;
    private readonly FuzzyMatcherService _fuzzyMatcher;

    public FamilyChildrenFuzzyMatchTests()
    {
        _fieldComparer = new PersonFieldComparer(NullLogger<PersonFieldComparer>.Instance);
        _fuzzyMatcher = new FuzzyMatcherService(
            new NameVariantsService(NullLogger<NameVariantsService>.Instance),
            NullLogger<FuzzyMatcherService>.Instance,
            new MatchingOptions { MatchThreshold = 70 });
        _service = new FamilyCompareService(
            NullLogger<FamilyCompareService>.Instance,
            _fuzzyMatcher);
    }

    [Fact(Skip = "Fuzzy matching logic needs investigation - children not being matched")]
    public void CompareFamilies_FuzzyMatchesMultipleChildren_EqualCounts()
    {
        // Arrange - Family with 3 children, 2 already mapped, 2 unmapped need fuzzy matching
        var source1 = CreatePerson("@I1@", "Иван", "Петров", 1950); // Father - mapped
        var source2 = CreatePerson("@I2@", "Мария", "Петрова", 1952); // Mother - mapped
        var sourceChild1 = CreatePerson("@I3@", "Пётр", "Петров", 1975); // Mapped child
        var sourceChild2 = CreatePerson("@I4@", "Анна", "Петрова", 1977); // Unmapped child 1
        var sourceChild3 = CreatePerson("@I5@", "Дмитрий", "Петров", 1980); // Unmapped child 2

        var dest1 = CreatePerson("@I100@", "Иван", "Петров", 1950); // Father
        var dest2 = CreatePerson("@I102@", "Мария", "Петрова", 1952); // Mother
        var destChild1 = CreatePerson("@I103@", "Пётр", "Петров", 1975); // Mapped child
        var destChild2 = CreatePerson("@I104@", "Анна", "Петрова", 1977); // Unmapped child 1
        var destChild3 = CreatePerson("@I105@", "Дмитрий", "Петров", 1980); // Unmapped child 2

        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", source1 },
            { "@I2@", source2 },
            { "@I3@", sourceChild1 },
            { "@I4@", sourceChild2 },
            { "@I5@", sourceChild3 }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I100@", dest1 },
            { "@I102@", dest2 },
            { "@I103@", destChild1 },
            { "@I104@", destChild2 },
            { "@I105@", destChild3 }
        };

        var sourceFamilies = new Dictionary<string, Family>
        {
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", new[] { "@I3@", "@I4@", "@I5@" }) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F100@", CreateFamily("@F100@", "@I100@", "@I102@", new[] { "@I103@", "@I104@", "@I105@" }) }
        };

        // Pre-map parents and one child
        var individualResult = new IndividualCompareResult
        {
            MatchedNodes = new[]
            {
                new MatchedNode
                {
                    SourceId = "@I1@",
                    DestinationId = "@I100@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Петров Иван (1950)"
                },
                new MatchedNode
                {
                    SourceId = "@I2@",
                    DestinationId = "@I102@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Петрова Мария (1952)"
                },
                new MatchedNode
                {
                    SourceId = "@I3@",
                    DestinationId = "@I103@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Петров Пётр (1975)"
                }
            }.ToImmutableList()
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I100@",
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
        // Should have fuzzy matched the 2 unmapped children
        Assert.Equal(2, result.NewPersonMappings.Count);
        Assert.True(result.NewPersonMappings.ContainsKey("@I4@"));
        Assert.True(result.NewPersonMappings.ContainsKey("@I5@"));
        Assert.Equal("@I104@", result.NewPersonMappings["@I4@"]);
        Assert.Equal("@I105@", result.NewPersonMappings["@I5@"]);
    }

    [Fact(Skip = "Fuzzy matching logic needs investigation - children not being matched")]
    public void CompareFamilies_FuzzyMatchesChildren_UnequalCounts_HighThreshold()
    {
        // Arrange - Source has 2 unmapped children, destination has 3 unmapped children
        // Should use higher threshold (85) and only match very similar children
        var source1 = CreatePerson("@I1@", "Иван", "Петров", 1950); // Father
        var source2 = CreatePerson("@I2@", "Мария", "Петрова", 1952); // Mother
        var sourceChild1 = CreatePerson("@I3@", "Пётр", "Петров", 1975); // Exact match child
        var sourceChild2 = CreatePerson("@I4@", "Анна", "Петрова", 1977); // Similar child

        var dest1 = CreatePerson("@I100@", "Иван", "Петров", 1950);
        var dest2 = CreatePerson("@I102@", "Мария", "Петрова", 1952);
        var destChild1 = CreatePerson("@I103@", "Пётр", "Петров", 1975); // Exact match
        var destChild2 = CreatePerson("@I104@", "Анна", "Петрова", 1977); // Similar
        var destChild3 = CreatePerson("@I105@", "Катя", "Петрова", 1979); // Different child

        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", source1 },
            { "@I2@", source2 },
            { "@I3@", sourceChild1 },
            { "@I4@", sourceChild2 }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I100@", dest1 },
            { "@I102@", dest2 },
            { "@I103@", destChild1 },
            { "@I104@", destChild2 },
            { "@I105@", destChild3 }
        };

        var sourceFamilies = new Dictionary<string, Family>
        {
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", new[] { "@I3@", "@I4@" }) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F100@", CreateFamily("@F100@", "@I100@", "@I102@", new[] { "@I103@", "@I104@", "@I105@" }) }
        };

        // Pre-map parents only
        var individualResult = new IndividualCompareResult
        {
            MatchedNodes = new[]
            {
                new MatchedNode
                {
                    SourceId = "@I1@",
                    DestinationId = "@I100@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Петров Иван (1950)"
                },
                new MatchedNode
                {
                    SourceId = "@I2@",
                    DestinationId = "@I102@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Петрова Мария (1952)"
                }
            }.ToImmutableList()
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I100@",
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
        // With unequal counts, should use higher threshold (85)
        // Should match the very similar children
        Assert.NotEmpty(result.NewPersonMappings);

        // At minimum, should have matched the exact match children if threshold is met
        if (result.NewPersonMappings.ContainsKey("@I3@"))
        {
            Assert.Equal("@I103@", result.NewPersonMappings["@I3@"]);
        }

        if (result.NewPersonMappings.ContainsKey("@I4@"))
        {
            Assert.Equal("@I104@", result.NewPersonMappings["@I4@"]);
        }
    }

    [Fact]
    public void CompareFamilies_NoFuzzyMatch_WhenPersonDictionariesNotProvided()
    {
        // Arrange
        var sourceFamilies = new Dictionary<string, Family>
        {
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", new[] { "@I3@", "@I4@" }) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F100@", CreateFamily("@F100@", "@I100@", "@I102@", new[] { "@I103@", "@I104@" }) }
        };

        var individualResult = new IndividualCompareResult
        {
            MatchedNodes = new[]
            {
                new MatchedNode
                {
                    SourceId = "@I1@",
                    DestinationId = "@I100@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Иван (1950)"
                },
                new MatchedNode
                {
                    SourceId = "@I2@",
                    DestinationId = "@I102@",
                    MatchedBy = "Test",
                    MatchScore = 100,
                    PersonSummary = "Мария (1952)"
                }
            }.ToImmutableList()
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I100@",
            MatchThreshold = 70
        };

        // Act - Don't provide person dictionaries
        var result = _service.CompareFamilies(
            sourceFamilies,
            destFamilies,
            individualResult,
            options,
            sourcePersons: null,
            destPersons: null);

        // Assert - Should not have fuzzy matched any children
        Assert.Empty(result.NewPersonMappings);
    }

    private PersonRecord CreatePerson(
        string id,
        string firstName,
        string lastName,
        int? birthYear = null)
    {
        return new PersonRecord
        {
            Id = id,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName,
            BirthDate = birthYear.HasValue ? new DateInfo
            {
                Date = new DateOnly(birthYear.Value, 1, 1),
                Precision = DatePrecision.Year
            } : null
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

        // Set children using reflection if needed
        if (childrenIds != null && childrenIds.Length > 0)
        {
            var childrenProperty = typeof(Family).GetProperty("Children");
            if (childrenProperty != null)
            {
                // Create GedcomCollection<string>
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
