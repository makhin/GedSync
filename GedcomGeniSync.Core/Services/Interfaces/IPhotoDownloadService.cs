namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for photo download service
/// Handles downloading photos from supported URLs
/// </summary>
public interface IPhotoDownloadService
{
    /// <summary>
    /// Check if URL is a supported photo URL
    /// </summary>
    bool IsSupportedPhotoUrl(string url);

    /// <summary>
    /// Download photo from a supported URL
    /// </summary>
    Task<PhotoDownloadResult?> DownloadPhotoAsync(string url);
}
