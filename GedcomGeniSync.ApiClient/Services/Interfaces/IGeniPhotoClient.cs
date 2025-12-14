using GedcomGeniSync.ApiClient.Models;

namespace GedcomGeniSync.ApiClient.Services.Interfaces;

/// <summary>
/// Interface for Geni Photo API operations
/// Provides CRUD operations for photos on Geni.com
/// </summary>
public interface IGeniPhotoClient
{
    // Photo Read Operations
    Task<List<GeniPhoto>> GetPhotosAsync(string profileId);
    Task<List<GeniPhotoTag>> GetPhotoTagsAsync(string photoId);

    // Photo Upload Operations
    Task<GeniPhoto?> AddPhotoAsync(string profileId, string filePath, string? caption = null);
    Task<GeniPhoto?> AddPhotoFromBytesAsync(string profileId, byte[] imageData, string fileName, string? caption = null);

    // Mugshot Operations
    Task<GeniPhoto?> SetMugshotAsync(string profileId, string filePath);
    Task<GeniPhoto?> SetMugshotFromBytesAsync(string profileId, byte[] imageData, string fileName);
    Task<bool> SetExistingPhotoAsMugshotAsync(string profileId, string photoId);

    // Photo Modification Operations
    Task<GeniPhoto?> UpdatePhotoAsync(string photoId, GeniPhotoUpdate update);
    Task<bool> DeletePhotoAsync(string photoId);

    // Photo Tagging Operations
    Task<bool> TagPhotoAsync(string photoId, string profileId, PhotoTagPosition? position = null);
    Task<bool> UntagPhotoAsync(string photoId, string profileId);
}
