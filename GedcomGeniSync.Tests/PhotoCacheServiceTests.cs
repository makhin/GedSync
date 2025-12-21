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
