using FluentAssertions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;

namespace GedcomGeniSync.Tests;

public class PhotoCacheModelsTests
{
    [Fact]
    public void PhotoCacheEntry_ShouldStoreValues()
    {
        var downloadedAt = DateTime.UtcNow;
        var lastAccessedAt = downloadedAt.AddMinutes(10);

        var entry = new PhotoCacheEntry
        {
            Url = "https://example.com/photo.jpg",
            LocalPath = "myheritage/@I1@/photo.jpg",
            PersonId = "@I1@",
            Source = "myheritage",
            FileSize = 1234,
            ContentHash = "sha256:abc123",
            PerceptualHash = "phash:deadbeef",
            DownloadedAt = downloadedAt,
            LastAccessedAt = lastAccessedAt
        };

        entry.Url.Should().Be("https://example.com/photo.jpg");
        entry.LocalPath.Should().Be("myheritage/@I1@/photo.jpg");
        entry.PersonId.Should().Be("@I1@");
        entry.Source.Should().Be("myheritage");
        entry.FileSize.Should().Be(1234);
        entry.ContentHash.Should().Be("sha256:abc123");
        entry.PerceptualHash.Should().Be("phash:deadbeef");
        entry.DownloadedAt.Should().Be(downloadedAt);
        entry.LastAccessedAt.Should().Be(lastAccessedAt);
    }

    [Fact]
    public void PhotoCacheIndex_ShouldInitializeDefaults()
    {
        var index = new PhotoCacheIndex();

        index.Version.Should().Be(1);
        index.Entries.Should().NotBeNull();
        index.Entries.Should().BeEmpty();
    }

    [Fact]
    public void PhotoCompareResult_ShouldStoreValues()
    {
        var result = new PhotoCompareResult
        {
            SourceUrl = "https://source.example/photo.jpg",
            DestinationUrl = "https://dest.example/photo.jpg",
            Similarity = 0.98,
            IsMatch = true,
            Reason = "pHash match"
        };

        result.SourceUrl.Should().Be("https://source.example/photo.jpg");
        result.DestinationUrl.Should().Be("https://dest.example/photo.jpg");
        result.Similarity.Should().Be(0.98);
        result.IsMatch.Should().BeTrue();
        result.Reason.Should().Be("pHash match");
    }
}

public class PhotoConfigTests
{
    [Fact]
    public void PhotoConfig_ShouldHaveDefaults()
    {
        var config = new PhotoConfig();

        config.Enabled.Should().BeTrue();
        config.CacheDirectory.Should().Be("./photos");
        config.DownloadOnLoad.Should().BeTrue();
        config.SimilarityThreshold.Should().Be(0.95);
        config.MaxConcurrentDownloads.Should().Be(4);
    }

    [Fact]
    public void ConfigurationLoader_ShouldLoadPhotoConfigFromJson()
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"gedsync-photo-config-{Guid.NewGuid():N}.json");
        var json = """
        {
          "photo": {
            "enabled": false,
            "cacheDirectory": "C:/photos",
            "downloadOnLoad": false,
            "similarityThreshold": 0.9,
            "maxConcurrentDownloads": 8
          }
        }
        """;

        File.WriteAllText(jsonPath, json);

        try
        {
            var loader = new ConfigurationLoader();
            var config = loader.Load(jsonPath);

            config.Photo.Enabled.Should().BeFalse();
            config.Photo.CacheDirectory.Should().Be("C:/photos");
            config.Photo.DownloadOnLoad.Should().BeFalse();
            config.Photo.SimilarityThreshold.Should().Be(0.9);
            config.Photo.MaxConcurrentDownloads.Should().Be(8);
        }
        finally
        {
            if (File.Exists(jsonPath))
            {
                File.Delete(jsonPath);
            }
        }
    }
}
