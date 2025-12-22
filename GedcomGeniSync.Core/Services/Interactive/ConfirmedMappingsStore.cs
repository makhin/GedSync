using System.Text.Json;
using GedcomGeniSync.Core.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Core.Services.Interactive;

/// <summary>
/// Service for loading and saving user-confirmed mappings
/// </summary>
public class ConfirmedMappingsStore
{
    private readonly ILogger<ConfirmedMappingsStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfirmedMappingsStore(ILogger<ConfirmedMappingsStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load confirmed mappings from file
    /// </summary>
    public ConfirmedMappingsFile? LoadMappings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogInformation("Confirmed mappings file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var mappings = JsonSerializer.Deserialize<ConfirmedMappingsFile>(json, JsonOptions);

            if (mappings == null)
            {
                _logger.LogWarning("Failed to deserialize confirmed mappings from {FilePath}", filePath);
                return null;
            }

            _logger.LogInformation(
                "Loaded {Count} confirmed mappings from {FilePath}",
                mappings.Mappings.Count,
                filePath
            );

            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading confirmed mappings from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Save confirmed mappings to file
    /// </summary>
    public bool SaveMappings(string filePath, ConfirmedMappingsFile mappings)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(mappings, JsonOptions);
            File.WriteAllText(filePath, json);

            _logger.LogInformation(
                "Saved {Count} confirmed mappings to {FilePath}",
                mappings.Mappings.Count,
                filePath
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving confirmed mappings to {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Add or update a single mapping in the file
    /// </summary>
    public bool AddOrUpdateMapping(
        string filePath,
        UserConfirmedMapping mapping,
        string? sourceFile = null,
        string? destinationFile = null)
    {
        var mappingsFile = LoadMappings(filePath) ?? new ConfirmedMappingsFile
        {
            SourceFile = sourceFile,
            DestinationFile = destinationFile
        };

        // Remove existing mapping for this sourceId if exists
        mappingsFile.Mappings.RemoveAll(m => m.SourceId == mapping.SourceId);

        // Add new mapping
        mappingsFile.Mappings.Add(mapping);

        return SaveMappings(filePath, mappingsFile);
    }

    /// <summary>
    /// Get confirmed mappings as a dictionary for quick lookup
    /// </summary>
    public Dictionary<string, UserConfirmedMapping> GetMappingsDictionary(ConfirmedMappingsFile? mappingsFile)
    {
        if (mappingsFile == null)
        {
            return new Dictionary<string, UserConfirmedMapping>();
        }

        return mappingsFile.Mappings.ToDictionary(m => m.SourceId, m => m);
    }

    /// <summary>
    /// Get only confirmed (not rejected or skipped) mappings as anchors
    /// </summary>
    public Dictionary<string, string> GetConfirmedAnchors(ConfirmedMappingsFile? mappingsFile)
    {
        if (mappingsFile == null)
        {
            return new Dictionary<string, string>();
        }

        return mappingsFile.Mappings
            .Where(m => m.Type == ConfirmationType.Confirmed && m.DestinationId != null)
            .ToDictionary(m => m.SourceId, m => m.DestinationId!);
    }

    /// <summary>
    /// Get rejected source IDs (to exclude from matching)
    /// </summary>
    public HashSet<string> GetRejectedSourceIds(ConfirmedMappingsFile? mappingsFile)
    {
        if (mappingsFile == null)
        {
            return new HashSet<string>();
        }

        return mappingsFile.Mappings
            .Where(m => m.Type == ConfirmationType.Rejected)
            .Select(m => m.SourceId)
            .ToHashSet();
    }

    /// <summary>
    /// Get rejected pairs (sourceId, destinationId) to exclude from candidates
    /// </summary>
    public HashSet<(string sourceId, string destinationId)> GetRejectedPairs(ConfirmedMappingsFile? mappingsFile)
    {
        if (mappingsFile == null)
        {
            return new HashSet<(string, string)>();
        }

        return mappingsFile.Mappings
            .Where(m => m.Type == ConfirmationType.Rejected && m.DestinationId != null)
            .Select(m => (m.SourceId, m.DestinationId!))
            .ToHashSet();
    }
}
