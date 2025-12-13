using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Compare;
using Microsoft.Extensions.Logging.Abstractions;
using Patagames.GedcomNetSdk.Records.Ver551;
using System.Collections.Generic;

namespace GedcomGeniSync.Tests.Services.Compare;

public class FamilyCompareServiceTests
{
    private readonly FamilyCompareService _service;

    public FamilyCompareServiceTests()
    {
        var fuzzyMatcher = new FuzzyMatcherService(
            new NameVariantsService(NullLogger<NameVariantsService>.Instance),
            NullLogger<FuzzyMatcherService>.Instance,
            new MatchingOptions { MatchThreshold = 70 });
        _service = new FamilyCompareService(NullLogger<FamilyCompareService>.Instance, fuzzyMatcher);
    }

    [Fact]
    public void CompareFamilies_ShouldSkipDeleteSuggestions_WhenDisabled()
    {
        var result = _service.CompareFamilies(
            new Dictionary<string, Family>(),
            new Dictionary<string, Family>
            {
                ["@F1@"] = CreateFamily("@F1@")
            },
            new IndividualCompareResult(),
            CreateOptions(includeDeletes: false));

        result.FamiliesToDelete.Should().BeEmpty();
    }

    [Fact]
    public void CompareFamilies_ShouldIncludeDeleteSuggestions_WhenEnabled()
    {
        var result = _service.CompareFamilies(
            new Dictionary<string, Family>(),
            new Dictionary<string, Family>
            {
                ["@F1@"] = CreateFamily("@F1@")
            },
            new IndividualCompareResult(),
            CreateOptions(includeDeletes: true));

        result.FamiliesToDelete.Should().ContainSingle()
            .Which.DestinationFamId.Should().Be("@F1@");
    }

    private static CompareOptions CreateOptions(bool includeDeletes) => new()
    {
        AnchorSourceId = "@I1@",
        AnchorDestinationId = "@I2@",
        IncludeDeleteSuggestions = includeDeletes
    };

    private static Family CreateFamily(string id, string? husbandId = null, string? wifeId = null)
    {
        return new Family
        {
            FamilyId = id,
            HusbandId = husbandId,
            WifeId = wifeId
        };
    }
}
