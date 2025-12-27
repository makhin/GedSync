using Microsoft.ML.Data;

namespace GedcomGeniSync.Services.ML;

/// <summary>
/// Training data for name locale classification
/// Each record represents a name with its known locale
/// </summary>
public class NameTrainingData
{
    /// <summary>
    /// The name text (first name, last name, or full name)
    /// </summary>
    [LoadColumn(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The locale/language of the name (e.g., "ru", "en", "he", "uk")
    /// </summary>
    [LoadColumn(1)]
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Name type: "first", "last", "middle", "maiden"
    /// </summary>
    [LoadColumn(2)]
    public string NameType { get; set; } = string.Empty;

    /// <summary>
    /// Gender if known: "male", "female", "unknown"
    /// </summary>
    [LoadColumn(3)]
    public string Gender { get; set; } = "unknown";
}

/// <summary>
/// Prediction result from the name locale classifier
/// </summary>
public class NameLocalePrediction
{
    /// <summary>
    /// Predicted locale code
    /// </summary>
    [ColumnName("PredictedLabel")]
    public string PredictedLocale { get; set; } = string.Empty;

    /// <summary>
    /// Confidence scores for each possible locale
    /// </summary>
    public float[] Score { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Detected script of a name
/// </summary>
public enum NameScript
{
    Unknown,
    Latin,      // A-Z, includes German umlauts, French accents, etc.
    Cyrillic,   // Russian, Ukrainian, Bulgarian, etc.
    Hebrew,     // Hebrew alphabet
    Arabic,     // Arabic script
    Greek,      // Greek alphabet
    Mixed       // Multiple scripts detected
}
