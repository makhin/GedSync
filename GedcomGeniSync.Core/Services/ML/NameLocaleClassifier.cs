using Microsoft.ML;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.ML;

/// <summary>
/// ML.NET-based classifier for predicting name locales
/// Uses character n-grams for text featurization
/// </summary>
public class NameLocaleClassifier
{
    private readonly MLContext _mlContext;
    private readonly ILogger _logger;
    private ITransformer? _model;
    private PredictionEngine<NameTrainingData, NameLocalePrediction>? _predictionEngine;
    private string[]? _labelNames;

    public NameLocaleClassifier(ILogger logger)
    {
        _mlContext = new MLContext(seed: 42);
        _logger = logger;
    }

    /// <summary>
    /// Whether the model has been trained
    /// </summary>
    public bool IsTrained => _model != null;

    /// <summary>
    /// Train the classifier from training data
    /// </summary>
    public void Train(IEnumerable<NameTrainingData> trainingData)
    {
        _logger.LogInformation("Starting model training...");

        var dataList = trainingData.ToList();
        if (dataList.Count == 0)
        {
            throw new InvalidOperationException("No training data provided");
        }

        _logger.LogInformation("Training with {Count} samples", dataList.Count);

        // Load data into IDataView
        var data = _mlContext.Data.LoadFromEnumerable(dataList);

        // Build training pipeline
        // 1. Map Locale string to key (required for multiclass classification)
        // 2. Featurize the name using character n-grams (works well for names)
        // 3. Append name type and gender as additional features
        // 4. Train multiclass classifier
        // 5. Map predicted key back to string

        var pipeline = _mlContext.Transforms.Conversion
            .MapValueToKey("Label", nameof(NameTrainingData.Locale))
            .Append(_mlContext.Transforms.Text.FeaturizeText(
                "NameFeatures",
                new Microsoft.ML.Transforms.Text.TextFeaturizingEstimator.Options
                {
                    CharFeatureExtractor = new Microsoft.ML.Transforms.Text.WordBagEstimator.Options
                    {
                        NgramLength = 3,  // Character trigrams work well for names
                        UseAllLengths = true
                    },
                    WordFeatureExtractor = null  // Don't use word features for single names
                },
                nameof(NameTrainingData.Name)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "NameTypeEncoded", nameof(NameTrainingData.NameType)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "GenderEncoded", nameof(NameTrainingData.Gender)))
            .Append(_mlContext.Transforms.Concatenate(
                "Features", "NameFeatures", "NameTypeEncoded", "GenderEncoded"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                "PredictedLabel", "PredictedLabel"));

        // Train the model
        _model = pipeline.Fit(data);

        // Extract label names for confidence reporting
        var labelColumn = data.Schema.GetColumnOrNull("Locale");
        if (labelColumn.HasValue)
        {
            var uniqueLabels = dataList.Select(d => d.Locale).Distinct().OrderBy(l => l).ToArray();
            _labelNames = uniqueLabels;
        }

        // Create prediction engine
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<NameTrainingData, NameLocalePrediction>(_model);

        _logger.LogInformation("Model training complete. Labels: {Labels}",
            string.Join(", ", _labelNames ?? Array.Empty<string>()));
    }

    /// <summary>
    /// Train with cross-validation and report metrics
    /// </summary>
    public void TrainWithValidation(IEnumerable<NameTrainingData> trainingData, int folds = 5)
    {
        var dataList = trainingData.ToList();
        _logger.LogInformation("Training with {Folds}-fold cross-validation on {Count} samples",
            folds, dataList.Count);

        var data = _mlContext.Data.LoadFromEnumerable(dataList);

        var pipeline = _mlContext.Transforms.Conversion
            .MapValueToKey("Label", nameof(NameTrainingData.Locale))
            .Append(_mlContext.Transforms.Text.FeaturizeText(
                "NameFeatures",
                new Microsoft.ML.Transforms.Text.TextFeaturizingEstimator.Options
                {
                    CharFeatureExtractor = new Microsoft.ML.Transforms.Text.WordBagEstimator.Options
                    {
                        NgramLength = 3,
                        UseAllLengths = true
                    },
                    WordFeatureExtractor = null
                },
                nameof(NameTrainingData.Name)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "NameTypeEncoded", nameof(NameTrainingData.NameType)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "GenderEncoded", nameof(NameTrainingData.Gender)))
            .Append(_mlContext.Transforms.Concatenate(
                "Features", "NameFeatures", "NameTypeEncoded", "GenderEncoded"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                "PredictedLabel", "PredictedLabel"));

        // Cross-validate
        var cvResults = _mlContext.MulticlassClassification.CrossValidate(
            data, pipeline, numberOfFolds: folds, labelColumnName: "Label");

        // Report metrics
        var avgMicroAccuracy = cvResults.Average(r => r.Metrics.MicroAccuracy);
        var avgMacroAccuracy = cvResults.Average(r => r.Metrics.MacroAccuracy);
        var avgLogLoss = cvResults.Average(r => r.Metrics.LogLoss);

        _logger.LogInformation("Cross-validation results ({Folds} folds):", folds);
        _logger.LogInformation("  Micro Accuracy: {Accuracy:P2}", avgMicroAccuracy);
        _logger.LogInformation("  Macro Accuracy: {Accuracy:P2}", avgMacroAccuracy);
        _logger.LogInformation("  Log Loss: {LogLoss:F4}", avgLogLoss);

        // Now train on full dataset
        Train(trainingData);
    }

    /// <summary>
    /// Predict the locale for a name
    /// </summary>
    public NameLocalePrediction Predict(string name, string nameType = "first", string gender = "unknown")
    {
        if (_predictionEngine == null)
        {
            throw new InvalidOperationException("Model has not been trained. Call Train() first.");
        }

        var input = new NameTrainingData
        {
            Name = name,
            NameType = nameType,
            Gender = gender
        };

        return _predictionEngine.Predict(input);
    }

    /// <summary>
    /// Predict with confidence information
    /// </summary>
    public (string Locale, float Confidence, Dictionary<string, float> AllScores) PredictWithConfidence(
        string name, string nameType = "first", string gender = "unknown")
    {
        var prediction = Predict(name, nameType, gender);

        var allScores = new Dictionary<string, float>();
        if (_labelNames != null && prediction.Score != null)
        {
            for (int i = 0; i < Math.Min(_labelNames.Length, prediction.Score.Length); i++)
            {
                allScores[_labelNames[i]] = prediction.Score[i];
            }
        }

        var confidence = prediction.Score?.Max() ?? 0f;
        return (prediction.PredictedLocale, confidence, allScores);
    }

    /// <summary>
    /// Save the trained model to a file
    /// </summary>
    public void SaveModel(string path)
    {
        if (_model == null)
        {
            throw new InvalidOperationException("Model has not been trained. Call Train() first.");
        }

        _mlContext.Model.Save(_model, null, path);
        _logger.LogInformation("Model saved to {Path}", path);

        // Also save label names
        var labelsPath = Path.ChangeExtension(path, ".labels");
        if (_labelNames != null)
        {
            File.WriteAllLines(labelsPath, _labelNames);
            _logger.LogInformation("Labels saved to {Path}", labelsPath);
        }
    }

    /// <summary>
    /// Load a previously trained model from a file
    /// </summary>
    public void LoadModel(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Model file not found: {path}");
        }

        _model = _mlContext.Model.Load(path, out _);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<NameTrainingData, NameLocalePrediction>(_model);

        // Load label names
        var labelsPath = Path.ChangeExtension(path, ".labels");
        if (File.Exists(labelsPath))
        {
            _labelNames = File.ReadAllLines(labelsPath);
        }

        _logger.LogInformation("Model loaded from {Path}", path);
    }
}
