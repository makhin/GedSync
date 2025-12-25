using System;

namespace GedcomGeniSync.Utils;

/// <summary>
/// Helper class for working with profile IDs across different formats.
/// Geni uses two types of IDs:
/// - Internal ID: Short numeric ID (10-12 digits) like "34852237013"
/// - GUID: Long numeric ID (16+ digits) like "6000000207133980253"
///
/// API URL formats:
/// - For internal IDs: profile-{id} (e.g., profile-34852237013)
/// - For GUIDs: profile-g{guid} (e.g., profile-g6000000207133980253)
/// </summary>
public static class ProfileIdHelper
{
    private const int GuidMinLength = 16; // GUIDs are typically 16+ digits

    /// <summary>
    /// Cleans profile ID by converting to Geni API format (g{numeric_id})
    /// Used when storing/comparing profile IDs.
    /// </summary>
    /// <param name="profileId">Profile ID that may contain prefixes like "geni:", "profile-", "profile-g", or "I" (MyHeritage format)</param>
    /// <returns>Profile ID in format g{numeric_id}</returns>
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
    /// Extracts profile ID for use in API URLs.
    /// Geni API accepts two formats:
    /// - "g{guid}" for GUIDs (long numbers like g6000000207133980253)
    /// - "{internal_id}" for internal IDs (shorter numbers like 34852237013)
    ///
    /// Examples:
    /// - "profile-34852237013" → "34852237013" (internal ID)
    /// - "profile-g6000000207133980253" → "g6000000207133980253" (GUID, keep 'g')
    /// - "g6000000207133980253" → "g6000000207133980253" (GUID, keep 'g')
    /// - "g34852237013" → "34852237013" (internal ID, remove 'g')
    /// </summary>
    public static string ExtractProfileIdForUrl(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return profileId;

        var id = profileId;

        // Remove "profile-" prefix if present
        if (id.StartsWith("profile-", StringComparison.OrdinalIgnoreCase))
        {
            id = id.Substring(8);
        }

        // Handle 'g' prefix: keep for GUIDs, remove for internal IDs
        if ((id.StartsWith('g') || id.StartsWith('G')) && id.Length > 1 && char.IsDigit(id[1]))
        {
            var numericPart = id.Substring(1);
            if (numericPart.Length >= GuidMinLength)
            {
                // It's a GUID, keep 'g' prefix
                return id;
            }
            else
            {
                // It's an internal ID, remove 'g' prefix
                return numericPart;
            }
        }

        return id;
    }

    /// <summary>
    /// Normalizes profile ID to a consistent format for comparison.
    /// Returns just the numeric part without any prefixes.
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

        // Remove leading 'g' prefix
        if (normalized.StartsWith('g') || normalized.StartsWith('G'))
        {
            normalized = normalized[1..];
        }

        // Remove leading 'I' if present (MyHeritage format)
        if ((normalized.StartsWith('I') || normalized.StartsWith('i')) &&
            normalized.Length > 1 && char.IsDigit(normalized[1]))
        {
            normalized = normalized[1..];
        }

        // Remove @ symbols if present (from GEDCOM IDs like @I123@)
        normalized = normalized.Replace("@", "");

        return normalized;
    }

    /// <summary>
    /// Checks if the given profile ID is a GUID (long format) or internal ID (short format).
    /// </summary>
    public static bool IsGuid(string profileId)
    {
        var normalized = NormalizeProfileId(profileId);
        return normalized.Length >= GuidMinLength;
    }
}
