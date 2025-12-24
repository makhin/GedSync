using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Checks for duplicate profiles via Geni API before adding new profiles
/// </summary>
public class ApiDuplicateChecker
{
    private readonly IGeniProfileClient _apiClient;
    private readonly ILogger<ApiDuplicateChecker> _logger;
    private readonly string? _cacheFilePath;
    private readonly Dictionary<string, DuplicateCheckResult?> _cache;

    public ApiDuplicateChecker(
        IGeniProfileClient apiClient,
        ILogger<ApiDuplicateChecker> logger,
        string? cacheFilePath = null)
    {
        _apiClient = apiClient;
        _logger = logger;
        _cacheFilePath = cacheFilePath;
        _cache = new Dictionary<string, DuplicateCheckResult?>();

        if (!string.IsNullOrWhiteSpace(_cacheFilePath))
        {
            LoadCache();
        }
    }

    /// <summary>
    /// Check if a profile already exists on Geni by querying the API
    /// </summary>
    /// <param name="nodeToAdd">Node being considered for addition</param>
    /// <param name="relativeGeniId">Geni profile ID of the relative to which we're adding</param>
    /// <returns>Information about found duplicate, or null if no duplicate found</returns>
    public async Task<DuplicateCheckResult?> CheckForDuplicateAsync(NodeToAdd nodeToAdd, string relativeGeniId)
    {
        // Check cache first
        if (_cache.TryGetValue(nodeToAdd.SourceId, out var cachedResult))
        {
            _logger.LogDebug("Using cached result for {SourceId}", nodeToAdd.SourceId);
            return cachedResult;
        }

        try
        {
            // Get immediate family of the relative
            var family = await _apiClient.GetImmediateFamilyAsync(relativeGeniId);
            if (family?.Nodes == null)
            {
                _logger.LogWarning("Could not fetch immediate family for {ProfileId}", relativeGeniId);
                return null; // Can't verify, assume not duplicate
            }

            DuplicateCheckResult? result = null;

            // Check all profiles in immediate family
            // The immediate family already contains relevant relatives (parents, children, spouses)
            foreach (var node in family.Nodes.Values)
            {
                if (IsMatch(nodeToAdd.PersonData, node))
                {
                    _logger.LogInformation(
                        "Found potential duplicate for {SourceId} ({Name}): Geni profile {GeniId}",
                        nodeToAdd.SourceId,
                        $"{nodeToAdd.PersonData.FirstName} {nodeToAdd.PersonData.LastName}",
                        node.Id);

                    result = new DuplicateCheckResult
                    {
                        GeniProfileId = node.Id ?? string.Empty,
                        GeniProfileName = $"{node.FirstName} {node.LastName}".Trim(),
                        GeniProfileUrl = BuildGeniProfileUrl(node.Id),
                        FoundViaGeniId = relativeGeniId
                    };
                    break;
                }
            }

            // Add to cache and save
            _cache[nodeToAdd.SourceId] = result;
            SaveCache();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for duplicate via API for {SourceId}", nodeToAdd.SourceId);
            return null; // On error, assume not duplicate to avoid blocking
        }
    }

    private bool IsMatch(PersonData personData, GeniNode geniNode)
    {
        // First name must match (case-insensitive, ignoring whitespace)
        if (!string.IsNullOrWhiteSpace(personData.FirstName) &&
            !string.IsNullOrWhiteSpace(geniNode.FirstName))
        {
            if (!NormalizeName(personData.FirstName).Equals(
                NormalizeName(geniNode.FirstName),
                StringComparison.OrdinalIgnoreCase))
            {
                return false; // First name doesn't match
            }
        }

        // Last name match (check both LastName and MaidenName)
        if (!string.IsNullOrWhiteSpace(personData.LastName))
        {
            var sourceLastName = NormalizeName(personData.LastName);
            var geniLastName = NormalizeName(geniNode.LastName);
            var geniMaidenName = NormalizeName(geniNode.MaidenName);

            if (!string.IsNullOrWhiteSpace(geniLastName) &&
                !sourceLastName.Equals(geniLastName, StringComparison.OrdinalIgnoreCase) &&
                !sourceLastName.Equals(geniMaidenName, StringComparison.OrdinalIgnoreCase))
            {
                return false; // Last name doesn't match
            }
        }

        // If we got here, it's a potential match
        return true;
    }

    private string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return name.Trim()
            .Replace("  ", " ")
            .ToLowerInvariant();
    }

    private static string? BuildGeniProfileUrl(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        // Geni profile URLs are in the format: https://www.geni.com/profile/index/{profileId}
        return $"https://www.geni.com/profile/index/{profileId}";
    }

    /// <summary>
    /// Load cache from file
    /// </summary>
    private void LoadCache()
    {
        if (string.IsNullOrWhiteSpace(_cacheFilePath) || !File.Exists(_cacheFilePath))
        {
            _logger.LogDebug("Cache file not found: {CacheFile}", _cacheFilePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var cache = JsonSerializer.Deserialize<Dictionary<string, DuplicateCheckResult?>>(json);

            if (cache != null)
            {
                foreach (var kvp in cache)
                {
                    _cache[kvp.Key] = kvp.Value;
                }
                _logger.LogInformation("Loaded {Count} entries from cache file: {CacheFile}",
                    _cache.Count, _cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache from {CacheFile}", _cacheFilePath);
        }
    }

    /// <summary>
    /// Save cache to file
    /// </summary>
    private void SaveCache()
    {
        if (string.IsNullOrWhiteSpace(_cacheFilePath))
            return;

        try
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_cacheFilePath, json);
            _logger.LogDebug("Saved cache to {CacheFile} ({Count} entries)", _cacheFilePath, _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save cache to {CacheFile}", _cacheFilePath);
        }
    }
}

/// <summary>
/// Result of checking for duplicate via Geni API
/// </summary>
public record DuplicateCheckResult
{
    /// <summary>Geni profile ID that was found</summary>
    public required string GeniProfileId { get; init; }

    /// <summary>Name of the Geni profile</summary>
    public required string GeniProfileName { get; init; }

    /// <summary>URL to the Geni profile</summary>
    public string? GeniProfileUrl { get; init; }

    /// <summary>Geni ID of the relative through which this duplicate was found</summary>
    public required string FoundViaGeniId { get; init; }
}
