using System.Diagnostics.CodeAnalysis;

namespace GedcomGeniSync.Services;

/// <summary>
/// Result of photo download operation.
/// </summary>
[ExcludeFromCodeCoverage]
public class PhotoDownloadResult
{
    /// <summary>
    /// Original URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// File name extracted from URL or generated.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Photo data as byte array.
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Content type (e.g., "image/jpeg").
    /// </summary>
    public required string ContentType { get; init; }
}
