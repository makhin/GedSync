using System.Text.Json;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Core.Models.Wave;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

public class WaveReportLoader
{
    private readonly ILogger<WaveReportLoader> _logger;

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WaveReportLoader(ILogger<WaveReportLoader> logger)
    {
        _logger = logger;
    }

    public async Task<WaveHighConfidenceReport?> LoadAsync(string inputPath)
    {
        _logger.LogInformation("Loading wave-compare report from {Path}...", inputPath);
        if (!File.Exists(inputPath))
        {
            _logger.LogError("Input file not found: {Path}", inputPath);
            return null;
        }

        var jsonContent = await File.ReadAllTextAsync(inputPath);

        var wrapper = JsonSerializer.Deserialize<WaveCompareJsonWrapper>(jsonContent, CaseInsensitiveOptions);
        var report = wrapper?.Report ??
                     JsonSerializer.Deserialize<WaveHighConfidenceReport>(jsonContent, CaseInsensitiveOptions);

        if (report == null)
        {
            _logger.LogError("Failed to parse wave-compare report. Expected JSON from wave-compare command or standalone report.");
        }

        return report;
    }
}
