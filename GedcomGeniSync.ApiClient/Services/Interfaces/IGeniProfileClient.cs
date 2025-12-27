using GedcomGeniSync.ApiClient.Models;

namespace GedcomGeniSync.ApiClient.Services.Interfaces;

/// <summary>
/// Interface for Geni Profile API operations
/// Provides CRUD operations for profiles on Geni.com
/// </summary>
public interface IGeniProfileClient
{
    // Profile Read Operations
    Task<GeniProfile?> GetProfileAsync(string profileId);
    Task<Dictionary<string, GeniProfile>> GetProfilesBatchAsync(List<string> profileIds);
    Task<GeniProfile?> GetCurrentUserProfileAsync();
    Task<GeniImmediateFamily?> GetImmediateFamilyAsync(string profileId);
    Task<List<GeniProfile>> SearchProfilesAsync(string name, string? birthYear = null);

    // Union Read Operations
    Task<Dictionary<string, GeniUnion>> GetUnionsBatchAsync(List<string> unionIds);

    // Profile Write Operations
    Task<GeniProfile?> AddChildAsync(string parentProfileId, GeniProfileCreate child);
    Task<GeniProfile?> AddParentAsync(string childProfileId, GeniProfileCreate parent);
    Task<GeniProfile?> AddPartnerAsync(string profileId, GeniProfileCreate partner);
    Task<GeniProfile?> AddChildToUnionAsync(string unionId, GeniProfileCreate child);
    Task<GeniProfile?> AddPartnerToUnionAsync(string unionId, GeniProfileCreate partner);
    Task<GeniProfile?> UpdateProfileAsync(string profileId, GeniProfileUpdate update);

    // Cache Operations
    /// <summary>
    /// Gets a cached profile if available
    /// </summary>
    GeniProfile? GetCachedProfile(string profileId);

    /// <summary>
    /// Gets a cached union if available
    /// </summary>
    GeniUnion? GetCachedUnion(string unionId);

    /// <summary>
    /// Gets cache statistics (unique profile count, unique union count)
    /// </summary>
    (int ProfileCount, int UnionCount) GetCacheStats();

    /// <summary>
    /// Clears the profile and union cache
    /// </summary>
    void ClearCache();
}
