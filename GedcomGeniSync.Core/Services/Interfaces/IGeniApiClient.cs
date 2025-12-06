namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for Geni API Client
/// Provides operations for profiles and photos on Geni.com
/// </summary>
public interface IGeniApiClient
{
    // Profile Read Operations
    Task<GeniProfile?> GetProfileAsync(string profileId);
    Task<GeniProfile?> GetCurrentUserProfileAsync();
    Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId);
    Task<List<GeniProfile>> SearchProfilesAsync(string name, string? birthYear = null);

    // Profile Write Operations
    Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child);
    Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent);
    Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner);
    Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child);
    Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner);

    // Photo Operations
    Task<List<GeniPhoto>> GetPhotosAsync(string profileId);
    Task<GeniPhoto?> AddPhotoAsync(string profileId, string filePath, string? caption = null);
    Task<GeniPhoto?> AddPhotoFromBytesAsync(string profileId, byte[] imageData, string fileName, string? caption = null);
    Task<GeniPhoto?> SetMugshotAsync(string profileId, string filePath);
    Task<GeniPhoto?> SetMugshotFromBytesAsync(string profileId, byte[] imageData, string fileName);
    Task<bool> SetExistingPhotoAsMugshotAsync(string profileId, string photoId);
    Task<GeniPhoto?> UpdatePhotoAsync(string photoId, GeniPhotoUpdate update);
    Task<bool> DeletePhotoAsync(string photoId);
    Task<bool> TagPhotoAsync(string photoId, string profileId, PhotoTagPosition? position = null);
    Task<bool> UntagPhotoAsync(string photoId, string profileId);
    Task<List<GeniPhotoTag>> GetPhotoTagsAsync(string photoId);
}
