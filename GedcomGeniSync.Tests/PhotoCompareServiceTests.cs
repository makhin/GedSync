using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Photo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GedcomGeniSync.Tests;

public class PhotoCompareServiceTests
{
    private const string SourcePersonId = "@S1@";
    private const string DestinationPersonId = "@D1@";

    [Fact]
    public async Task ComparePersonPhotosAsync_ShouldIdentifyExactMatches()
    {
        var sourceEntry = CreateEntry("https://src/photo1.jpg", "src/photo1.jpg");
        var destinationEntry = CreateEntry("https://dest/photo1.jpg", "dest/photo1.jpg", DestinationPersonId);

        var cacheMock = CreateCacheMock(
            new[] { sourceEntry },
            new[] { destinationEntry },
            new Dictionary<string, byte[]>
            {
                [sourceEntry.LocalPath] = new byte[] { 1, 2, 3 },
                [destinationEntry.LocalPath] = new byte[] { 1, 2, 3 }
            });

        var service = CreateService(cacheMock);

        var report = await service.ComparePersonPhotosAsync(
            SourcePersonId,
            new[] { sourceEntry.Url },
            DestinationPersonId,
            new[] { destinationEntry.Url });

        report.NewPhotos.Should().BeEmpty();
        report.SimilarPhotos.Should().BeEmpty();
        report.MatchedPhotos.Should().ContainSingle();
        report.MatchedPhotos[0].IsMatch.Should().BeTrue();
        report.MatchedPhotos[0].Similarity.Should().Be(1.0);
        report.MatchedPhotos[0].SourceLocalPath.Should().Be(sourceEntry.LocalPath);
        report.MatchedPhotos[0].DestinationLocalPath.Should().Be(destinationEntry.LocalPath);
    }

    [Fact]
    public async Task ComparePersonPhotosAsync_ShouldDetectSimilarPhotos()
    {
        var sourceEntry = CreateEntry("https://src/photo1.jpg", "src/photo1.jpg");
        var destinationEntry = CreateEntry("https://dest/photo2.jpg", "dest/photo2.jpg", DestinationPersonId);

        var cacheMock = CreateCacheMock(
            new[] { sourceEntry },
            new[] { destinationEntry },
            new Dictionary<string, byte[]>
            {
                [sourceEntry.LocalPath] = new byte[] { 1 },
                [destinationEntry.LocalPath] = new byte[] { 3 } // differs by one bit
            });

        var service = CreateService(cacheMock);

        var report = await service.ComparePersonPhotosAsync(
            SourcePersonId,
            new[] { sourceEntry.Url },
            DestinationPersonId,
            new[] { destinationEntry.Url });

        report.MatchedPhotos.Should().BeEmpty();
        report.NewPhotos.Should().BeEmpty();
        report.SimilarPhotos.Should().ContainSingle();
        report.SimilarPhotos[0].IsMatch.Should().BeFalse();
        report.SimilarPhotos[0].Similarity.Should().BeGreaterThan(0.95);
        report.SimilarPhotos[0].SourceLocalPath.Should().Be(sourceEntry.LocalPath);
        report.SimilarPhotos[0].DestinationLocalPath.Should().Be(destinationEntry.LocalPath);
    }

    [Fact]
    public async Task ComparePersonPhotosAsync_ShouldReturnNewPhotosWhenDestinationMissing()
    {
        var sourceEntry = CreateEntry("https://src/photo1.jpg", "src/photo1.jpg");

        var cacheMock = CreateCacheMock(
            new[] { sourceEntry },
            Array.Empty<PhotoCacheEntry>(),
            new Dictionary<string, byte[]>
            {
                [sourceEntry.LocalPath] = new byte[] { 5, 6, 7 }
            });

        var service = CreateService(cacheMock);

        var report = await service.ComparePersonPhotosAsync(
            SourcePersonId,
            new[] { sourceEntry.Url },
            DestinationPersonId,
            Array.Empty<string>());

        report.MatchedPhotos.Should().BeEmpty();
        report.SimilarPhotos.Should().BeEmpty();
        report.NewPhotos.Should().ContainSingle().Which.Url.Should().Be(sourceEntry.Url);
    }

    [Fact]
    public async Task ComparePersonPhotosAsync_ShouldReturnEmptyReportWhenDisabled()
    {
        var cacheMock = new Mock<IPhotoCacheService>(MockBehavior.Strict);
        var config = new PhotoConfig { Enabled = false };
        var service = new PhotoCompareService(
            config,
            cacheMock.Object,
            new TestPhotoHashService(),
            NullLogger<PhotoCompareService>.Instance);

        var report = await service.ComparePersonPhotosAsync(
            SourcePersonId,
            Array.Empty<string>(),
            DestinationPersonId,
            Array.Empty<string>());

        report.NewPhotos.Should().BeEmpty();
        report.MatchedPhotos.Should().BeEmpty();
        report.SimilarPhotos.Should().BeEmpty();
    }

    private static PhotoCompareService CreateService(Mock<IPhotoCacheService> cacheMock)
    {
        var config = new PhotoConfig
        {
            Enabled = true,
            SimilarityThreshold = 0.95
        };

        return new PhotoCompareService(
            config,
            cacheMock.Object,
            new TestPhotoHashService(),
            NullLogger<PhotoCompareService>.Instance);
    }

    private static Mock<IPhotoCacheService> CreateCacheMock(
        IReadOnlyList<PhotoCacheEntry> sourceEntries,
        IReadOnlyList<PhotoCacheEntry> destinationEntries,
        Dictionary<string, byte[]> photoData)
    {
        var mock = new Mock<IPhotoCacheService>();

        mock.Setup(s => s.EnsureDownloadedAsync(SourcePersonId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(sourceEntries);

        mock.Setup(s => s.EnsureDownloadedAsync(DestinationPersonId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(destinationEntries);

        mock.Setup(s => s.GetPhotoDataByPathAsync(It.IsAny<string>()))
            .ReturnsAsync((string path) =>
                photoData.TryGetValue(path, out var bytes)
                    ? bytes
                    : null);

        mock.Setup(s => s.UpdateEntry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));

        mock.Setup(s => s.SaveIndexAsync())
            .Returns(Task.CompletedTask);

        return mock;
    }

    private static PhotoCacheEntry CreateEntry(string url, string localPath, string personId = SourcePersonId)
    {
        return new PhotoCacheEntry
        {
            Url = url,
            LocalPath = localPath,
            PersonId = personId,
            Source = "myheritage",
            FileSize = 0,
            ContentHash = null,
            PerceptualHash = null,
            DownloadedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
    }

    private sealed class TestPhotoHashService : IPhotoHashService
    {
        public string ComputeContentHash(byte[] data)
        {
            return "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        public ulong ComputePerceptualHash(byte[] imageData)
        {
            Span<byte> buffer = stackalloc byte[8];
            buffer.Clear();
            var copyLength = Math.Min(imageData.Length, 8);
            imageData.AsSpan(0, copyLength).CopyTo(buffer);
            return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        public double CompareHashes(ulong hash1, ulong hash2)
        {
            var diff = hash1 ^ hash2;
            var distance = BitOperations.PopCount(diff);
            return 1.0 - (distance / 64.0);
        }
    }
}
