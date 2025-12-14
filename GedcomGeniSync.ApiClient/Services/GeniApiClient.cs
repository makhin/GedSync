using System.Diagnostics.CodeAnalysis;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;

namespace GedcomGeniSync.ApiClient.Services;

/// <summary>
/// Geni API Client - Composite implementation
/// Delegates to specialized clients for profile and photo operations
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniApiClient : IGeniApiClient
{
    private readonly IGeniProfileClient _profileClient;
    private readonly IGeniPhotoClient _photoClient;

    public GeniApiClient(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
    }

    #region Profile Operations - Delegated to IGeniProfileClient

    public Task<GeniProfile?> GetProfileAsync(string profileId)
        => _profileClient.GetProfileAsync(profileId);

    public Task<Dictionary<string, GeniProfile>> GetProfilesBatchAsync(List<string> profileIds)
        => _profileClient.GetProfilesBatchAsync(profileIds);

    public Task<GeniProfile?> GetCurrentUserProfileAsync()
        => _profileClient.GetCurrentUserProfileAsync();

    public Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId)
        => _profileClient.GetImmediateFamilyAsync(profileId);

    public Task<List<GeniProfile>> SearchProfilesAsync(string name, string? birthYear = null)
        => _profileClient.SearchProfilesAsync(name, birthYear);

    public Task<Dictionary<string, GeniUnion>> GetUnionsBatchAsync(List<string> unionIds)
        => _profileClient.GetUnionsBatchAsync(unionIds);

    public Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child)
        => _profileClient.AddChildAsync(parentProfileId, child);

    public Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent)
        => _profileClient.AddParentAsync(childProfileId, parent);

    public Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner)
        => _profileClient.AddPartnerAsync(profileId, partner);

    public Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child)
        => _profileClient.AddChildToUnionAsync(unionId, child);

    public Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner)
        => _profileClient.AddPartnerToUnionAsync(unionId, partner);

    public Task<GeniProfile?> UpdateProfileAsync(string profileId, GeniProfileUpdate update)
        => _profileClient.UpdateProfileAsync(profileId, update);

    #endregion

    #region Photo Operations - Delegated to IGeniPhotoClient

    public Task<List<GeniPhoto>> GetPhotosAsync(string profileId)
        => _photoClient.GetPhotosAsync(profileId);

    public Task<List<GeniPhotoTag>> GetPhotoTagsAsync(string photoId)
        => _photoClient.GetPhotoTagsAsync(photoId);

    public Task<GeniPhoto?> AddPhotoAsync(string profileId, string filePath, string? caption = null)
        => _photoClient.AddPhotoAsync(profileId, filePath, caption);

    public Task<GeniPhoto?> AddPhotoFromBytesAsync(string profileId, byte[] imageData, string fileName, string? caption = null)
        => _photoClient.AddPhotoFromBytesAsync(profileId, imageData, fileName, caption);

    public Task<GeniPhoto?> SetMugshotAsync(string profileId, string filePath)
        => _photoClient.SetMugshotAsync(profileId, filePath);

    public Task<GeniPhoto?> SetMugshotFromBytesAsync(string profileId, byte[] imageData, string fileName)
        => _photoClient.SetMugshotFromBytesAsync(profileId, imageData, fileName);

    public Task<bool> SetExistingPhotoAsMugshotAsync(string profileId, string photoId)
        => _photoClient.SetExistingPhotoAsMugshotAsync(profileId, photoId);

    public Task<GeniPhoto?> UpdatePhotoAsync(string photoId, GeniPhotoUpdate update)
        => _photoClient.UpdatePhotoAsync(photoId, update);

    public Task<bool> DeletePhotoAsync(string photoId)
        => _photoClient.DeletePhotoAsync(photoId);

    public Task<bool> TagPhotoAsync(string photoId, string profileId, PhotoTagPosition? position = null)
        => _photoClient.TagPhotoAsync(photoId, profileId, position);

    public Task<bool> UntagPhotoAsync(string photoId, string profileId)
        => _photoClient.UntagPhotoAsync(photoId, profileId);

    #endregion
}
