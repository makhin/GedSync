using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GedcomGeniSync.Services;

/// <summary>
/// Service for loading configuration from JSON or YAML files
/// </summary>
public class ConfigurationLoader
{
    private readonly ILogger<ConfigurationLoader>? _logger;

    public ConfigurationLoader(ILogger<ConfigurationLoader>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load configuration from file (auto-detects JSON or YAML by extension)
    /// </summary>
    public GedSyncConfiguration Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        var extension = Path.GetExtension(configPath).ToLowerInvariant();

        _logger?.LogInformation("Loading configuration from {Path}", configPath);

        return extension switch
        {
            ".json" => LoadFromJson(configPath),
            ".yaml" or ".yml" => LoadFromYaml(configPath),
            _ => throw new NotSupportedException($"Unsupported configuration file format: {extension}. Use .json, .yaml, or .yml")
        };
    }

    /// <summary>
    /// Try to load configuration from file, returns null if file doesn't exist
    /// </summary>
    public GedSyncConfiguration? TryLoad(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger?.LogDebug("Configuration file not found: {Path}", configPath);
            return null;
        }

        try
        {
            return Load(configPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load configuration from {Path}", configPath);
            return null;
        }
    }

    /// <summary>
    /// Try to load configuration from multiple possible paths
    /// Returns the first successfully loaded configuration or a default one
    /// </summary>
    public GedSyncConfiguration LoadWithDefaults(params string[] possiblePaths)
    {
        foreach (var path in possiblePaths)
        {
            var config = TryLoad(path);
            if (config != null)
            {
                _logger?.LogInformation("Using configuration from {Path}", path);
                return config;
            }
        }

        _logger?.LogInformation("No configuration file found, using defaults");
        return new GedSyncConfiguration();
    }

    /// <summary>
    /// Load configuration from JSON file
    /// </summary>
    private GedSyncConfiguration LoadFromJson(string path)
    {
        var json = File.ReadAllText(path);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var config = JsonSerializer.Deserialize<GedSyncConfiguration>(json, options);

        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize configuration from {path}");
        }

        _logger?.LogDebug("Configuration loaded from JSON: {Path}", path);
        return config;
    }

    /// <summary>
    /// Load configuration from YAML file
    /// </summary>
    private GedSyncConfiguration LoadFromYaml(string path)
    {
        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<GedSyncConfiguration>(yaml);

        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize configuration from {path}");
        }

        _logger?.LogDebug("Configuration loaded from YAML: {Path}", path);
        return config;
    }

    /// <summary>
    /// Save configuration to file (auto-detects format by extension)
    /// </summary>
    public void Save(GedSyncConfiguration config, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        switch (extension)
        {
            case ".json":
                SaveToJson(config, path);
                break;
            case ".yaml":
            case ".yml":
                SaveToYaml(config, path);
                break;
            default:
                throw new NotSupportedException($"Unsupported configuration file format: {extension}. Use .json, .yaml, or .yml");
        }

        _logger?.LogInformation("Configuration saved to {Path}", path);
    }

    /// <summary>
    /// Save configuration to JSON file
    /// </summary>
    private void SaveToJson(GedSyncConfiguration config, string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Save configuration to YAML file
    /// </summary>
    private void SaveToYaml(GedSyncConfiguration config, string path)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(config);
        File.WriteAllText(path, yaml);
    }
}
