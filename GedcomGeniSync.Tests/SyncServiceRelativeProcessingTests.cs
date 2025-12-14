using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GedcomGeniSync.Tests;

public class SyncServiceRelativeProcessingTests
{
    [Fact]
    public async Task ProcessRelativesAsync_ProcessesAllRelationTypes()
    {
        var service = CreateService();
        var person = new PersonRecord
        {
            Id = "I1",
            Source = PersonSource.Gedcom,
            FatherId = "F1",
            MotherId = "M1",
            SpouseIds = ImmutableList.Create("S1", "S2"),
            ChildrenIds = ImmutableList.Create("C1", "C2")
        };

        await service.InvokeProcessRelativesAsync(
            person,
            "G1",
            null,
            new GedcomLoadResult(),
            new Queue<(string GedcomId, string GeniId, int Depth)>(),
            0,
            CancellationToken.None);

        service.Calls.Should().BeEquivalentTo(
            new[]
            {
                new RelativeCall("F1", RelationType.Parent, Gender.Male),
                new RelativeCall("M1", RelationType.Parent, Gender.Female),
                new RelativeCall("S1", RelationType.Partner, Gender.Unknown),
                new RelativeCall("S2", RelationType.Partner, Gender.Unknown),
                new RelativeCall("C1", RelationType.Child, Gender.Unknown),
                new RelativeCall("C2", RelationType.Child, Gender.Unknown)
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ProcessRelativesAsync_SkipsEmptyIdentifiers()
    {
        var service = CreateService();
        var person = new PersonRecord
        {
            Id = "I2",
            Source = PersonSource.Gedcom,
            MotherId = string.Empty,
            SpouseIds = ImmutableList.Create(string.Empty),
            ChildrenIds = ImmutableList.Create(string.Empty)
        };

        await service.InvokeProcessRelativesAsync(
            person,
            "G2",
            null,
            new GedcomLoadResult(),
            new Queue<(string GedcomId, string GeniId, int Depth)>(),
            1,
            CancellationToken.None);

        service.Calls.Should().BeEmpty();
    }

    private static TestableSyncService CreateService()
    {
        var gedcomLoader = Mock.Of<IGedcomLoader>();
        var geniClient = Mock.Of<IGeniApiClient>();
        var matcher = Mock.Of<IFuzzyMatcherService>();
        var stateManager = Mock.Of<ISyncStateManager>();
        var logger = Mock.Of<ILogger<SyncService>>();

        return new TestableSyncService(
            gedcomLoader,
            geniClient,
            matcher,
            stateManager,
            logger);
    }

    private sealed class TestableSyncService : SyncService
    {
        public List<RelativeCall> Calls { get; } = new();

        public TestableSyncService(
            IGedcomLoader gedcomLoader,
            IGeniApiClient geniClient,
            IFuzzyMatcherService matcher,
            ISyncStateManager stateManager,
            ILogger<SyncService> logger)
            : base(gedcomLoader, geniClient, matcher, stateManager, logger)
        {
        }

        protected override Task ProcessRelativeAsync(
            string relativeGedId,
            string currentGeniId,
            RelationType relationType,
            Gender expectedGender,
            GeniImmediateFamily? geniFamily,
            GedcomLoadResult gedcomData,
            Queue<(string GedcomId, string GeniId, int Depth)> queue,
            int currentDepth,
            CancellationToken cancellationToken)
        {
            Calls.Add(new RelativeCall(relativeGedId, relationType, expectedGender));
            return Task.CompletedTask;
        }

        public Task InvokeProcessRelativesAsync(
            PersonRecord currentPerson,
            string currentGeniId,
            GeniImmediateFamily? geniFamily,
            GedcomLoadResult gedcomData,
            Queue<(string GedcomId, string GeniId, int Depth)> queue,
            int currentDepth,
            CancellationToken cancellationToken)
        {
            var method = typeof(SyncService).GetMethod(
                "ProcessRelativesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ArgumentNullException.ThrowIfNull(method);

            return (Task)method.Invoke(
                this,
                new object[]
                {
                    currentPerson,
                    currentGeniId,
                    geniFamily,
                    gedcomData,
                    queue,
                    currentDepth,
                    cancellationToken
                })!;
        }
    }

    private readonly record struct RelativeCall(
        string RelativeId,
        RelationType RelationType,
        Gender ExpectedGender);
}
