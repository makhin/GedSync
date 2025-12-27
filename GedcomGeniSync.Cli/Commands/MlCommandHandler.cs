using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.ML;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

/// <summary>
/// Command handler for ML-related operations:
/// - export-training-data: Export training data from GEDCOM
/// - train-model: Train ML model from CSV
/// - predict: Predict locale for a name
/// </summary>
public class MlCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    // Export subcommand options
    private readonly Option<string> _gedcomOption = new("--gedcom", "Path to GEDCOM file") { IsRequired = true };
    private readonly Option<string> _outputOption = new("--output", "Output CSV file path") { IsRequired = true };

    // Train subcommand options
    private readonly Option<string> _csvOption = new("--csv", "Path to training CSV file") { IsRequired = true };
    private readonly Option<string> _modelOption = new("--model", "Output model file path") { IsRequired = true };
    private readonly Option<bool> _validateOption = new("--validate", () => true, "Perform cross-validation during training");

    // Predict subcommand options
    private readonly Option<string> _nameOption = new("--name", "Name to classify") { IsRequired = true };
    private readonly Option<string> _modelPathOption = new("--model", "Path to trained model file") { IsRequired = true };
    private readonly Option<string> _typeOption = new("--type", () => "first", "Name type: first, last, middle, maiden");
    private readonly Option<string> _genderOption = new("--gender", () => "unknown", "Gender: male, female, unknown");

    public MlCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var mlCommand = new Command("ml", "Machine learning operations for name classification");

        // Export training data subcommand
        var exportCommand = new Command("export", "Export training data from GEDCOM file");
        exportCommand.AddOption(_gedcomOption);
        exportCommand.AddOption(_outputOption);
        exportCommand.SetHandler(HandleExportAsync);
        mlCommand.AddCommand(exportCommand);

        // Train model subcommand
        var trainCommand = new Command("train", "Train ML model from CSV training data");
        trainCommand.AddOption(_csvOption);
        trainCommand.AddOption(_modelOption);
        trainCommand.AddOption(_validateOption);
        trainCommand.SetHandler(HandleTrainAsync);
        mlCommand.AddCommand(trainCommand);

        // Predict subcommand
        var predictCommand = new Command("predict", "Predict locale for a name using trained model");
        predictCommand.AddOption(_nameOption);
        predictCommand.AddOption(_modelPathOption);
        predictCommand.AddOption(_typeOption);
        predictCommand.AddOption(_genderOption);
        predictCommand.SetHandler(HandlePredictAsync);
        mlCommand.AddCommand(predictCommand);

        return mlCommand;
    }

    private async Task HandleExportAsync(InvocationContext context)
    {
        var gedcomPath = context.ParseResult.GetValueForOption(_gedcomOption)!;
        var outputPath = context.ParseResult.GetValueForOption(_outputOption)!;

        await using var scope = _startup.CreateScope(verbose: true);
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ML");

        try
        {
            logger.LogInformation("=== Export Training Data ===");
            logger.LogInformation("GEDCOM: {Path}", gedcomPath);
            logger.LogInformation("Output: {Path}", outputPath);

            var gedcomLoader = scope.ServiceProvider.GetRequiredService<IGedcomLoader>();
            var exporter = new GedcomTrainingDataExporter(gedcomLoader, logger);

            await exporter.ExportToCsvAsync(gedcomPath, outputPath);

            logger.LogInformation("Export complete!");
            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export failed");
            context.ExitCode = 1;
        }
    }

    private async Task HandleTrainAsync(InvocationContext context)
    {
        var csvPath = context.ParseResult.GetValueForOption(_csvOption)!;
        var modelPath = context.ParseResult.GetValueForOption(_modelOption)!;
        var validate = context.ParseResult.GetValueForOption(_validateOption);

        await using var scope = _startup.CreateScope(verbose: true);
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ML");

        try
        {
            logger.LogInformation("=== Train ML Model ===");
            logger.LogInformation("Training data: {Path}", csvPath);
            logger.LogInformation("Model output: {Path}", modelPath);
            logger.LogInformation("Cross-validation: {Validate}", validate);

            // Load training data from CSV
            var trainingData = LoadTrainingDataFromCsv(csvPath, logger);
            logger.LogInformation("Loaded {Count} training samples", trainingData.Count);

            var classifier = new NameLocaleClassifier(logger);

            if (validate)
            {
                classifier.TrainWithValidation(trainingData);
            }
            else
            {
                classifier.Train(trainingData);
            }

            classifier.SaveModel(modelPath);

            logger.LogInformation("Training complete!");
            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Training failed");
            context.ExitCode = 1;
        }

        await Task.CompletedTask;
    }

    private async Task HandlePredictAsync(InvocationContext context)
    {
        var name = context.ParseResult.GetValueForOption(_nameOption)!;
        var modelPath = context.ParseResult.GetValueForOption(_modelPathOption)!;
        var nameType = context.ParseResult.GetValueForOption(_typeOption)!;
        var gender = context.ParseResult.GetValueForOption(_genderOption)!;

        await using var scope = _startup.CreateScope(verbose: true);
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ML");

        try
        {
            logger.LogInformation("=== Predict Name Locale ===");
            logger.LogInformation("Name: {Name}", name);
            logger.LogInformation("Type: {Type}", nameType);
            logger.LogInformation("Gender: {Gender}", gender);

            var classifier = new NameLocaleClassifier(logger);
            classifier.LoadModel(modelPath);

            var (locale, confidence, allScores) = classifier.PredictWithConfidence(name, nameType, gender);

            logger.LogInformation("Predicted locale: {Locale} (confidence: {Confidence:P2})", locale, confidence);
            logger.LogInformation("All scores:");
            foreach (var (loc, score) in allScores.OrderByDescending(kv => kv.Value))
            {
                logger.LogInformation("  {Locale}: {Score:P2}", loc, score);
            }

            // Also show script detection result for comparison
            var script = ScriptDetector.DetectScript(name);
            var scriptLocale = ScriptDetector.InferLocaleFromScript(script);
            logger.LogInformation("Script detection: {Script} -> {Locale}", script, scriptLocale);

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prediction failed");
            context.ExitCode = 1;
        }

        await Task.CompletedTask;
    }

    private static List<NameTrainingData> LoadTrainingDataFromCsv(string path, ILogger logger)
    {
        var data = new List<NameTrainingData>();
        var lines = File.ReadAllLines(path);

        // Skip header
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = ParseCsvLine(line);
            if (parts.Length >= 2)
            {
                data.Add(new NameTrainingData
                {
                    Name = parts[0],
                    Locale = parts[1],
                    NameType = parts.Length > 2 ? parts[2] : "first",
                    Gender = parts.Length > 3 ? parts[3] : "unknown"
                });
            }
        }

        return data;
    }

    private static string[] ParseCsvLine(string line)
    {
        var parts = new List<string>();
        var current = "";
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                parts.Add(current);
                current = "";
            }
            else
            {
                current += c;
            }
        }

        parts.Add(current);
        return parts.ToArray();
    }
}
