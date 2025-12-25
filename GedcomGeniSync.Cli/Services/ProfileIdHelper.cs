using System;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Helper class for working with profile IDs across different formats.
/// Handles Geni API format (g{numeric_id}), MyHeritage format (I{numeric_id}),
/// and various prefixed formats (profile-, geni:, etc.)
/// </summary>
public static class ProfileIdHelper
{
    /// <summary>
    /// Cleans profile ID by converting to Geni API format (g{numeric_id})
    /// </summary>
    /// <param name="profileId">Profile ID that may contain prefixes like "geni:", "profile-", "profile-g", or "I" (MyHeritage format)</param>
    /// <returns>Profile ID in format g{numeric_id} for use in API URLs</returns>
    public static string CleanProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return profileId;

        // Extract numeric part
        var id = profileId.Contains(':')
            ? profileId[(profileId.LastIndexOf(':') + 1)..]
            : profileId.Replace("profile-", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Remove leading 'I' if present (MyHeritage/GEDCOM format like I6000000207133980253)
        if (id.StartsWith("I", StringComparison.OrdinalIgnoreCase) && id.Length > 1 && char.IsDigit(id[1]))
        {
            id = id.Substring(1);
        }

        // Ensure g prefix
        return id.StartsWith('g') ? id : $"g{id}";
    }

    /// <summary>
    /// Normalizes profile ID to a consistent format for comparison.
    /// Handles various formats:
    /// - Full URL: https://www.geni.com/api/profile-34828568625 → 34828568625
    /// - Prefixed: profile-g34828568625, profile-34828568625 → 34828568625
    /// - Short: g34828568625 → 34828568625
    /// - Numeric: 34828568625 → 34828568625
    /// </summary>
    public static string NormalizeProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return string.Empty;

        var normalized = profileId;

        // Handle full URL format: https://www.geni.com/api/profile-34828568625
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                normalized = normalized[(lastSlash + 1)..];
            }
        }

        // Remove "profile-g" prefix (must be before "profile-" to avoid partial match)
        if (normalized.StartsWith("profile-g", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[9..]; // Length of "profile-g"
        }
        // Remove "profile-" prefix
        else if (normalized.StartsWith("profile-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[8..]; // Length of "profile-"
        }

        // Remove leading 'g' prefix (not all 'g' characters!)
        if (normalized.StartsWith('g') || normalized.StartsWith('G'))
        {
            normalized = normalized[1..];
        }

        // Remove leading 'I' if present (MyHeritage format)
        if (normalized.StartsWith('I') || normalized.StartsWith('i'))
        {
            if (normalized.Length > 1 && char.IsDigit(normalized[1]))
            {
                normalized = normalized[1..];
            }
        }

        // Remove @ symbols if present (from GEDCOM IDs like @I123@)
        normalized = normalized.Replace("@", "");

        return normalized;
    }
}
