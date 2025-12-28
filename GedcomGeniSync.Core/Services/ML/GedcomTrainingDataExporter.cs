using System.Text;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.ML;

/// <summary>
/// Exports training data from GEDCOM files for ML.NET name classification
/// </summary>
public class GedcomTrainingDataExporter
{
    private readonly IGedcomLoader _loader;
    private readonly ILogger _logger;

    public GedcomTrainingDataExporter(IGedcomLoader loader, ILogger logger)
    {
        _loader = loader;
        _logger = logger;
    }

    /// <summary>
    /// Export training data from a GEDCOM file
    /// </summary>
    public async Task<List<NameTrainingData>> ExportFromGedcomAsync(string gedcomPath)
    {
        _logger.LogInformation("Loading GEDCOM file: {Path}", gedcomPath);

        // Load without downloading photos - we only need names
        var result = await _loader.LoadAsync(gedcomPath, downloadPhotos: false);
        var trainingData = new List<NameTrainingData>();
        var stats = new Dictionary<string, int>();

        foreach (var person in result.Persons.Values)
        {
            var records = ExtractTrainingRecords(person);
            foreach (var record in records)
            {
                trainingData.Add(record);
                stats.TryGetValue(record.Locale, out var count);
                stats[record.Locale] = count + 1;
            }
        }

        _logger.LogInformation("Extracted {Count} training records from {PersonCount} persons",
            trainingData.Count, result.Persons.Count);

        foreach (var (locale, count) in stats.OrderByDescending(kv => kv.Value))
        {
            _logger.LogInformation("  {Locale}: {Count} names", locale, count);
        }

        return trainingData;
    }

    /// <summary>
    /// Export training data to CSV file
    /// </summary>
    public async Task ExportToCsvAsync(string gedcomPath, string outputPath)
    {
        var data = await ExportFromGedcomAsync(gedcomPath);

        var sb = new StringBuilder();
        sb.AppendLine("Name,Locale,NameType,Gender");

        foreach (var record in data)
        {
            // Escape CSV fields
            var name = EscapeCsv(record.Name);
            var locale = EscapeCsv(record.Locale);
            var nameType = EscapeCsv(record.NameType);
            var gender = EscapeCsv(record.Gender);

            sb.AppendLine($"{name},{locale},{nameType},{gender}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("Exported {Count} records to {Path}", data.Count, outputPath);
    }

    // Names to exclude from training data (placeholders, not real names)
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "private", "unknown", "living", "deceased", "unnamed", "stillborn",
        "infant", "baby", "child", "son", "daughter", "mr", "mrs", "ms",
        "nn", "n.n.", "n.n", "?", "??", "???", "-", "--", "---"
    };

    /// <summary>
    /// Check if a name is valid for training (not a placeholder)
    /// </summary>
    private static bool IsValidTrainingName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();

        // Too short
        if (trimmed.Length < 2)
            return false;

        // Excluded placeholder names
        if (ExcludedNames.Contains(trimmed))
            return false;

        // Names that are just punctuation or numbers
        if (trimmed.All(c => !char.IsLetter(c)))
            return false;

        return true;
    }

    /// <summary>
    /// Extract training records from a single person
    /// </summary>
    private List<NameTrainingData> ExtractTrainingRecords(PersonRecord person)
    {
        var records = new List<NameTrainingData>();
        var gender = person.Gender.ToString().ToLowerInvariant();

        // Infer locale from place (most reliable)
        var placeLocale = ScriptDetector.InferLocaleFromPlace(person.BirthPlace)
                       ?? ScriptDetector.InferLocaleFromPlace(person.DeathPlace);

        // Extract first name
        if (IsValidTrainingName(person.FirstName))
        {
            var locale = DetermineLocale(person.FirstName!, placeLocale);
            if (locale != "unknown")
            {
                records.Add(new NameTrainingData
                {
                    Name = person.FirstName!.Trim(),
                    Locale = locale,
                    NameType = "first",
                    Gender = gender
                });
            }
        }

        // Extract middle name (patronymic for Russian names)
        if (IsValidTrainingName(person.MiddleName))
        {
            var locale = DetermineLocale(person.MiddleName!, placeLocale);
            if (locale != "unknown")
            {
                records.Add(new NameTrainingData
                {
                    Name = person.MiddleName!.Trim(),
                    Locale = locale,
                    NameType = "middle",
                    Gender = gender
                });
            }
        }

        // Extract last name
        if (IsValidTrainingName(person.LastName))
        {
            var locale = DetermineLocale(person.LastName!, placeLocale);
            if (locale != "unknown")
            {
                records.Add(new NameTrainingData
                {
                    Name = person.LastName!.Trim(),
                    Locale = locale,
                    NameType = "last",
                    Gender = gender
                });
            }
        }

        // Extract maiden name
        if (IsValidTrainingName(person.MaidenName))
        {
            var locale = DetermineLocale(person.MaidenName!, placeLocale);
            if (locale != "unknown")
            {
                records.Add(new NameTrainingData
                {
                    Name = person.MaidenName!.Trim(),
                    Locale = locale,
                    NameType = "maiden",
                    Gender = "female" // Maiden names are always for females
                });
            }
        }

        // Extract from name variants (transliterations)
        foreach (var variant in person.NameVariants)
        {
            if (!IsValidTrainingName(variant))
                continue;

            // Skip if already added
            if (records.Any(r => r.Name.Equals(variant.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;

            var locale = DetermineLocale(variant, placeLocale);
            if (locale != "unknown")
            {
                records.Add(new NameTrainingData
                {
                    Name = variant.Trim(),
                    Locale = locale,
                    NameType = "variant",
                    Gender = gender
                });
            }
        }

        return records;
    }

    /// <summary>
    /// Determine the most likely locale for a name
    /// Uses script detection + place-based validation
    /// </summary>
    private string DetermineLocale(string name, string? placeLocale)
    {
        var script = ScriptDetector.DetectScript(name);

        // Handle Cyrillic with more precision
        if (script == NameScript.Cyrillic)
        {
            // If we know the place is Ukrainian, use that
            if (placeLocale == "uk")
                return "uk";
            if (placeLocale == "be")
                return "be";

            // Otherwise analyze the name itself
            return ScriptDetector.InferCyrillicLocale(name);
        }

        // Handle Latin with more precision
        if (script == NameScript.Latin)
        {
            // Use place-based locale if available and it's a Latin-script language
            if (placeLocale is "en" or "de" or "pl" or "lt" or "lv" or "fr")
                return placeLocale;

            // Otherwise analyze character patterns
            return ScriptDetector.InferLatinLocale(name);
        }

        // For other scripts, use the default mapping
        return ScriptDetector.InferLocaleFromScript(script);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If contains comma, quote, or newline - wrap in quotes and escape quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
