using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for configuration loader service
/// Loads configuration from JSON or YAML files
/// </summary>
public interface IConfigurationLoader
{
    /// <summary>
    /// Load configuration from file (auto-detects JSON or YAML by extension)
    /// </summary>
    GedSyncConfiguration Load(string configPath);

    /// <summary>
    /// Try to load configuration from file, returns null if file doesn't exist
    /// </summary>
    GedSyncConfiguration? TryLoad(string configPath);

    /// <summary>
    /// Try to load configuration from multiple possible paths
    /// Returns the first successfully loaded configuration or a default one
    /// </summary>
    GedSyncConfiguration LoadWithDefaults(params string[] possiblePaths);

    /// <summary>
    /// Save configuration to file (auto-detects format by extension)
    /// </summary>
    void Save(GedSyncConfiguration config, string path);
}
