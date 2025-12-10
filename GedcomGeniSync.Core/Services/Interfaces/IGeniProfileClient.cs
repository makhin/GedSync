namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for Geni Profile API operations
/// Provides CRUD operations for profiles on Geni.com
/// </summary>
public interface IGeniProfileClient
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
    Task<GeniProfile?> UpdateProfileAsync(string profileId, GeniProfileUpdate update);
}
