using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using Patagames.GedcomNetSdk.Records.Ver551;
using Xunit;

namespace GedcomGeniSync.Tests.Services.Compare;

public class AmbiguousMatchResolutionTests
{
    private readonly IndividualCompareService _service;
    private readonly PersonFieldComparer _fieldComparer;
    private readonly FuzzyMatcherService _fuzzyMatcher;

    public AmbiguousMatchResolutionTests()
    {
        _fieldComparer = new PersonFieldComparer(NullLogger<PersonFieldComparer>.Instance);
        _fuzzyMatcher = new FuzzyMatcherService(
            new NameVariantsService(NullLogger<NameVariantsService>.Instance),
            NullLogger<FuzzyMatcherService>.Instance,
            new MatchingOptions { MatchThreshold = 70 });
        _service = new IndividualCompareService(
            NullLogger<IndividualCompareService>.Instance,
            _fieldComparer,
            _fuzzyMatcher);
    }

    [Fact]
    public void CompareIndividuals_AmbiguousMatch_ResolvedByFamilyContext()
    {
        // Arrange - Create two persons with same name and birth year (ambiguous)
        var source1 = CreatePerson("@I1@", "Иван", "Иванов", 1950);
        var source2 = CreatePerson("@I2@", "Мария", "Иванова", 1952); // Wife
        var source3 = CreatePerson("@I3@", "Петр", "Иванов", 1975);   // Son

        var dest1a = CreatePerson("@I100@", "Иван", "Иванов", 1950);  // First candidate
        var dest1b = CreatePerson("@I101@", "Иван", "Иванов", 1950);  // Second candidate (same score)
        var dest2 = CreatePerson("@I102@", "Мария", "Иванова", 1952); // Wife
        var dest3 = CreatePerson("@I103@", "Петр", "Иванов", 1975);   // Son

        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", source1 },
            { "@I2@", source2 },
            { "@I3@", source3 }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I100@", dest1a },
            { "@I101@", dest1b },
            { "@I102@", dest2 },
            { "@I103@", dest3 }
        };

        // Create families - dest1a is in family with already mapped wife and son
        var sourceFamilies = new Dictionary<string, Family>
        {
            { "@F1@", CreateFamily("@F1@", "@I1@", "@I2@", new[] { "@I3@" }) }
        };

        var destFamilies = new Dictionary<string, Family>
        {
            { "@F100@", CreateFamily("@F100@", "@I100@", "@I102@", new[] { "@I103@" }) }, // Family with dest1a
            { "@F101@", CreateFamily("@F101@", "@I101@", null, null) }  // Family with dest1b (no other members)
        };

        // Pre-map wife and son to establish family context
        var existingMappings = new Dictionary<string, string>
        {
            { "@I2@", "@I102@" },  // Wife mapped
            { "@I3@", "@I103@" }   // Son mapped
        };

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I100@",
            MatchThreshold = 70,
            RequireUniqueMatch = true
        };

        // Act
        var result = _service.CompareIndividuals(
            sourcePersons,
            destPersons,
            options,
            existingMappings,
            sourceFamilies,
            destFamilies);

        // Assert
        // Should resolve to @I100@ because it's in a family with already mapped members
        Assert.Empty(result.AmbiguousMatches);

        var matched = result.MatchedNodes
            .Concat<dynamic>(result.NodesToUpdate)
            .FirstOrDefault(n => n.SourceId == "@I1@");

        Assert.NotNull(matched);
        Assert.Equal("@I100@", matched.DestinationId);
        Assert.Equal("AmbiguousResolvedByFamily", matched.MatchedBy);
    }

    [Fact]
    public void CompareIndividuals_AmbiguousMatch_CannotResolve_RemainsAmbiguous()
    {
        // Arrange - Two candidates with equal family context
        var source1 = CreatePerson("@I1@", "Иван", "Иванов", 1950);

        var dest1a = CreatePerson("@I100@", "Иван", "Иванов", 1950);
        var dest1b = CreatePerson("@I101@", "Иван", "Иванов", 1950);

        var sourcePersons = new Dictionary<string, PersonRecord>
        {
            { "@I1@", source1 }
        };

        var destPersons = new Dictionary<string, PersonRecord>
        {
            { "@I100@", dest1a },
            { "@I101@", dest1b }
        };

        // No families or no mapped members - cannot resolve
        var sourceFamilies = new Dictionary<string, Family>();
        var destFamilies = new Dictionary<string, Family>();
        var existingMappings = new Dictionary<string, string>();

        var options = new CompareOptions
        {
            AnchorSourceId = "@I1@",
            AnchorDestinationId = "@I100@",
            MatchThreshold = 70,
            RequireUniqueMatch = true
        };

        // Act
        var result = _service.CompareIndividuals(
            sourcePersons,
            destPersons,
            options,
            existingMappings,
            sourceFamilies,
            destFamilies);

        // Assert - Should remain ambiguous
        Assert.Single(result.AmbiguousMatches);
        Assert.Equal("@I1@", result.AmbiguousMatches[0].SourceId);
        Assert.Equal(2, result.AmbiguousMatches[0].Candidates.Count);
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
