namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for MyHeritage photo download service
/// Handles downloading photos from MyHeritage URLs
/// </summary>
public interface IMyHeritagePhotoService
{
    /// <summary>
    /// Check if URL is a MyHeritage photo URL
    /// </summary>
    bool IsMyHeritageUrl(string url);

    /// <summary>
    /// Download photo from MyHeritage URL
    /// </summary>
    Task<PhotoDownloadResult?> DownloadPhotoAsync(string url);
}
