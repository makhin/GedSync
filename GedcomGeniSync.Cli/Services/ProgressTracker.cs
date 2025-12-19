using System.Text.Json;
using GedcomGeniSync.Cli.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Service for tracking and persisting command execution progress
/// </summary>
public class ProgressTracker
{
    private readonly ILogger<ProgressTracker> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProgressTracker(ILogger<ProgressTracker> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Get progress file path for UPDATE command
    /// </summary>
    public string GetUpdateProgressPath(string inputFile)
    {
        var inputFileName = Path.GetFileNameWithoutExtension(inputFile);
        return Path.Combine(Path.GetDirectoryName(inputFile) ?? ".", $"{inputFileName}.update-progress.json");
    }

    /// <summary>
    /// Get progress file path for ADD command
    /// </summary>
    public string GetAddProgressPath(string inputFile)
    {
        var inputFileName = Path.GetFileNameWithoutExtension(inputFile);
        return Path.Combine(Path.GetDirectoryName(inputFile) ?? ".", $"{inputFileName}.add-progress.json");
    }

    /// <summary>
    /// Load UPDATE progress from file
    /// </summary>
    public UpdateProgress? LoadUpdateProgress(string inputFile)
    {
        var progressPath = GetUpdateProgressPath(inputFile);
        if (!File.Exists(progressPath))
        {
            _logger.LogDebug("No progress file found at {Path}", progressPath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(progressPath);
            var progress = JsonSerializer.Deserialize<UpdateProgress>(json, _jsonOptions);

            if (progress != null)
            {
                _logger.LogInformation("Loaded progress: {Processed}/{Total} profiles processed",
                    progress.ProcessedSourceIds.Count, progress.TotalProfiles);
            }

            return progress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load progress from {Path}", progressPath);
            return null;
        }
    }

    /// <summary>
    /// Save UPDATE progress to file
    /// </summary>
    public void SaveUpdateProgress(string inputFile, UpdateProgress progress)
    {
        var progressPath = GetUpdateProgressPath(inputFile);

        try
        {
            var json = JsonSerializer.Serialize(progress, _jsonOptions);
            File.WriteAllText(progressPath, json);
            _logger.LogDebug("Progress saved to {Path}", progressPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save progress to {Path}", progressPath);
        }
    }

    /// <summary>
    /// Load ADD progress from file
    /// </summary>
    public AddProgress? LoadAddProgress(string inputFile)
    {
        var progressPath = GetAddProgressPath(inputFile);
        if (!File.Exists(progressPath))
        {
            _logger.LogDebug("No progress file found at {Path}", progressPath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(progressPath);
            var progress = JsonSerializer.Deserialize<AddProgress>(json, _jsonOptions);

            if (progress != null)
            {
                _logger.LogInformation("Loaded progress: {Processed}/{Total} profiles processed",
                    progress.ProcessedSourceIds.Count, progress.TotalProfiles);
            }

            return progress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load progress from {Path}", progressPath);
            return null;
        }
    }

    /// <summary>
    /// Save ADD progress to file
    /// </summary>
    public void SaveAddProgress(string inputFile, AddProgress progress)
    {
        var progressPath = GetAddProgressPath(inputFile);

        try
        {
            var json = JsonSerializer.Serialize(progress, _jsonOptions);
            File.WriteAllText(progressPath, json);
            _logger.LogDebug("Progress saved to {Path}", progressPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save progress to {Path}", progressPath);
        }
    }

    /// <summary>
    /// Delete UPDATE progress file
    /// </summary>
    public void DeleteUpdateProgress(string inputFile)
    {
        var progressPath = GetUpdateProgressPath(inputFile);
        if (File.Exists(progressPath))
        {
            try
            {
                File.Delete(progressPath);
                _logger.LogInformation("Progress file deleted: {Path}", progressPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete progress file {Path}", progressPath);
            }
        }
    }

    /// <summary>
    /// Delete ADD progress file
    /// </summary>
    public void DeleteAddProgress(string inputFile)
    {
        var progressPath = GetAddProgressPath(inputFile);
        if (File.Exists(progressPath))
        {
            try
            {
                File.Delete(progressPath);
                _logger.LogInformation("Progress file deleted: {Path}", progressPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete progress file {Path}", progressPath);
            }
        }
    }
}
