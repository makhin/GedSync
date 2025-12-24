using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Photo;
using Microsoft.Extensions.Logging.Abstractions;

namespace GedcomGeniSync.Tests;

public class PhotoCacheServiceTests
{
    [Fact]
    public async Task EnsureDownloadedAsync_ShouldCreateCacheEntryAndFile()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var url = "https://media.myheritage.com/photo.jpg";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [url] = CreateResult(url, "photo.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            var entry = await service.EnsureDownloadedAsync(url, "@I1@");

            entry.Should().NotBeNull();
            entry!.Source.Should().Be("myheritage");

            var fullPath = Path.Combine(cacheDir, entry.LocalPath);
            File.Exists(fullPath).Should().BeTrue();
            File.ReadAllBytes(fullPath).Should().Equal(data);

            File.Exists(Path.Combine(cacheDir, "cache.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task EnsureDownloadedAsync_ShouldReuseCachedEntry()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var url = "https://media.geni.com/p14/6f/3c/92/7a/photo.jpg";
        var data = new byte[] { 9, 8, 7 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [url] = CreateResult(url, "photo.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            var first = await service.EnsureDownloadedAsync(url, "@I2@");
            var second = await service.EnsureDownloadedAsync(url, "@I2@");

            first.Should().NotBeNull();
            first!.Source.Should().Be("geni");
            second.Should().NotBeNull();
            second!.LocalPath.Should().Be(first!.LocalPath);
            downloadService.DownloadCalls.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task UpdateEntry_ShouldUpdateHashesInCache()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var url = "https://media.myheritage.com/photo.jpg";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [url] = CreateResult(url, "photo.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            // Download photo
            var entry = await service.EnsureDownloadedAsync(url, "@I1@");
            entry.Should().NotBeNull();
            entry!.ContentHash.Should().BeNull();
            entry!.PerceptualHash.Should().BeNull();

            // Update with hashes
            service.UpdateEntry(url, "sha256:abc123", "phash:0x1234567890abcdef");
            await service.SaveIndexAsync();

            // Reload service to verify persistence
            var service2 = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);
            var reloadedEntry = service2.GetEntry(url);

            reloadedEntry.Should().NotBeNull();
            reloadedEntry!.ContentHash.Should().Be("sha256:abc123");
            reloadedEntry!.PerceptualHash.Should().Be("phash:0x1234567890abcdef");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task EnsureDownloadedAsync_ShouldUseSameCacheForGeniUrlsWithDifferentHashes()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var urlWithHash1 = "https://media.geni.com/p14/91/f8/photo.jpg?hash=abc.123";
        var urlWithHash2 = "https://media.geni.com/p14/91/f8/photo.jpg?hash=xyz.456";
        var urlNoHash = "https://media.geni.com/p14/91/f8/photo.jpg";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [urlWithHash1] = CreateResult(urlWithHash1, "photo.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            // Download with first hash
            var entry1 = await service.EnsureDownloadedAsync(urlWithHash1, "@I1@");
            entry1.Should().NotBeNull();

            // Second URL with different hash should use cached entry
            var entry2 = await service.EnsureDownloadedAsync(urlWithHash2, "@I1@");
            entry2.Should().NotBeNull();
            entry2!.LocalPath.Should().Be(entry1!.LocalPath);

            // URL without hash should also use cached entry
            var entry3 = await service.EnsureDownloadedAsync(urlNoHash, "@I1@");
            entry3.Should().NotBeNull();
            entry3!.LocalPath.Should().Be(entry1!.LocalPath);

            // Should only have downloaded once
            downloadService.DownloadCalls.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task RecordMatch_ShouldStoreMatchInfoForBothPhotos()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var sourceUrl = "https://media.myheritage.com/source.jpg";
        var destUrl = "https://media.geni.com/dest.jpg";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [sourceUrl] = CreateResult(sourceUrl, "source.jpg", data),
            [destUrl] = CreateResult(destUrl, "dest.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            await service.EnsureDownloadedAsync(sourceUrl, "@I1@");
            await service.EnsureDownloadedAsync(destUrl, "@I2@");

            service.RecordMatch(sourceUrl, destUrl);
            await service.SaveIndexAsync();

            // Reload and verify
            var service2 = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);
            var sourceEntry = service2.GetEntry(sourceUrl);
            var destEntry = service2.GetEntry(destUrl);

            sourceEntry.Should().NotBeNull();
            sourceEntry!.MatchedWithUrl.Should().Be(destUrl);
            sourceEntry.MatchedAt.Should().NotBeNull();

            destEntry.Should().NotBeNull();
            destEntry!.MatchedWithUrl.Should().Be(sourceUrl);
            destEntry.MatchedAt.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task IsAlreadyMatched_ShouldReturnTrueForMatchedPhotos()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var sourceUrl = "https://media.myheritage.com/source.jpg";
        var destUrl = "https://media.geni.com/dest.jpg";
        var otherUrl = "https://media.geni.com/other.jpg";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [sourceUrl] = CreateResult(sourceUrl, "source.jpg", data),
            [destUrl] = CreateResult(destUrl, "dest.jpg", data),
            [otherUrl] = CreateResult(otherUrl, "other.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            await service.EnsureDownloadedAsync(sourceUrl, "@I1@");
            await service.EnsureDownloadedAsync(destUrl, "@I2@");
            await service.EnsureDownloadedAsync(otherUrl, "@I3@");

            service.RecordMatch(sourceUrl, destUrl);

            service.IsAlreadyMatched(sourceUrl, destUrl).Should().BeTrue();
            service.IsAlreadyMatched(destUrl, sourceUrl).Should().BeTrue();
            service.IsAlreadyMatched(sourceUrl, otherUrl).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    [Fact]
    public async Task IsAlreadyMatched_ShouldNormalizeCacheKeysForGeniUrls()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"photo-cache-{Guid.NewGuid():N}");
        var sourceUrl = "https://media.myheritage.com/source.jpg";
        var destUrlWithHash = "https://media.geni.com/dest.jpg?hash=abc.123";
        var destUrlDifferentHash = "https://media.geni.com/dest.jpg?hash=xyz.456";
        var data = new byte[] { 1, 2, 3, 4 };

        var downloadService = new TestPhotoDownloadService(new Dictionary<string, PhotoDownloadResult>
        {
            [sourceUrl] = CreateResult(sourceUrl, "source.jpg", data),
            [destUrlWithHash] = CreateResult(destUrlWithHash, "dest.jpg", data)
        });

        var config = new PhotoConfig { CacheDirectory = cacheDir };
        var service = new PhotoCacheService(config, downloadService, NullLogger<PhotoCacheService>.Instance);

        try
        {
            await service.EnsureDownloadedAsync(sourceUrl, "@I1@");
            await service.EnsureDownloadedAsync(destUrlWithHash, "@I2@");

            service.RecordMatch(sourceUrl, destUrlWithHash);

            // Should match even with different hash parameter
            service.IsAlreadyMatched(sourceUrl, destUrlDifferentHash).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
    }

    private static PhotoDownloadResult CreateResult(string url, string fileName, byte[] data)
    {
        return new PhotoDownloadResult
        {
            Url = url,
            FileName = fileName,
            Data = data,
            ContentType = "image/jpeg"
        };
    }

    private sealed class TestPhotoDownloadService : IPhotoDownloadService
    {
        private readonly Dictionary<string, PhotoDownloadResult> _results;

        public int DownloadCalls { get; private set; }

        public TestPhotoDownloadService(Dictionary<string, PhotoDownloadResult> results)
        {
            _results = results;
        }

        public bool IsSupportedPhotoUrl(string url) => true;

        public Task<PhotoDownloadResult?> DownloadPhotoAsync(string url)
        {
            DownloadCalls++;
            return Task.FromResult(_results.TryGetValue(url, out var result) ? result : null);
        }
    }
}
