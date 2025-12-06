using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Service for downloading photos from MyHeritage
/// </summary>
[ExcludeFromCodeCoverage]
public class MyHeritagePhotoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MyHeritagePhotoService> _logger;
    private readonly bool _dryRun;

    private static readonly string[] MyHeritageHosts = new[]
    {
        "myheritage.com",
        "www.myheritage.com",
        "familysearch.myheritage.com",
        "media.myheritage.com"
    };

    public MyHeritagePhotoService(
        IHttpClientFactory httpClientFactory,
        ILogger<MyHeritagePhotoService> logger,
        bool dryRun = false)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dryRun = dryRun;
    }

    /// <summary>
    /// Check if URL is a MyHeritage photo URL
    /// </summary>
    public bool IsMyHeritageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            return MyHeritageHosts.Any(host =>
                uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Download photo from MyHeritage URL
    /// </summary>
    public async Task<PhotoDownloadResult?> DownloadPhotoAsync(string url)
    {
        if (!IsMyHeritageUrl(url))
        {
            _logger.LogWarning("URL is not a MyHeritage URL: {Url}", url);
            return null;
        }

        if (_dryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would download photo from: {Url}", url);
            return new PhotoDownloadResult
            {
                Url = url,
                FileName = "dry-run-photo.jpg",
                Data = Array.Empty<byte>(),
                ContentType = "image/jpeg"
            };
        }

        try
        {
            _logger.LogInformation("Downloading photo from MyHeritage: {Url}", url);

            using var client = _httpClientFactory.CreateClient("MyHeritagePhoto");

            // Set headers to mimic a browser request
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Referer", "https://www.myheritage.com/");

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download photo from {Url}: {StatusCode}",
                    url, response.StatusCode);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var fileName = ExtractFileNameFromUrl(url, contentType);

            _logger.LogInformation("Successfully downloaded photo: {FileName} ({Size} bytes)",
                fileName, data.Length);

            return new PhotoDownloadResult
            {
                Url = url,
                FileName = fileName,
                Data = data,
                ContentType = contentType
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error downloading photo from {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error downloading photo from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Extract file name from URL or generate based on content type
    /// </summary>
    private static string ExtractFileNameFromUrl(string url, string contentType)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            var lastSegment = segments.LastOrDefault()?.Trim('/');

            if (!string.IsNullOrEmpty(lastSegment) && lastSegment.Contains('.'))
            {
                return lastSegment;
            }

            // Generate filename based on content type
            var extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                _ => ".jpg"
            };

            return $"myheritage-photo-{Guid.NewGuid():N}{extension}";
        }
        catch
        {
            return $"myheritage-photo-{Guid.NewGuid():N}.jpg";
        }
    }

    /// <summary>
    /// Download multiple photos from MyHeritage URLs
    /// </summary>
    public async Task<List<PhotoDownloadResult>> DownloadPhotosAsync(IEnumerable<string> urls)
    {
        var results = new List<PhotoDownloadResult>();

        foreach (var url in urls.Where(IsMyHeritageUrl))
        {
            var result = await DownloadPhotoAsync(url);
            if (result != null)
            {
                results.Add(result);
            }
        }

        return results;
    }
}

/// <summary>
/// Result of photo download operation
/// </summary>
[ExcludeFromCodeCoverage]
public class PhotoDownloadResult
{
    /// <summary>
    /// Original URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// File name extracted from URL or generated
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Photo data as byte array
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Content type (e.g., "image/jpeg")
    /// </summary>
    public required string ContentType { get; init; }
}
