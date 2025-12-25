namespace GedcomGeniSync.Services.Interfaces;

/// <summary>
/// Service for normalizing Slavic surnames to handle gender-specific forms.
/// Converts feminine surname forms (e.g., Иванова, Kowalska) to their masculine equivalents (Иванов, Kowalski).
/// </summary>
public interface ISurnameNormalizer
{
    /// <summary>
    /// Normalizes a surname to its base (masculine) form.
    /// </summary>
    /// <param name="surname">The surname to normalize</param>
    /// <returns>Normalized surname in masculine form</returns>
    string Normalize(string? surname);

    /// <summary>
    /// Compares two surnames accounting for gender variations.
    /// </summary>
    /// <param name="surname1">First surname</param>
    /// <param name="surname2">Second surname</param>
    /// <returns>True if surnames match (ignoring gender suffix)</returns>
    bool AreEquivalent(string? surname1, string? surname2);

    /// <summary>
    /// Returns similarity score between two surnames (0.0 - 1.0).
    /// Returns 1.0 for equivalent surnames, falls back to provided similarity function otherwise.
    /// </summary>
    /// <param name="surname1">First surname</param>
    /// <param name="surname2">Second surname</param>
    /// <param name="fallbackSimilarity">Fallback similarity function (e.g., Jaro-Winkler)</param>
    /// <returns>Similarity score from 0.0 to 1.0</returns>
    double GetSimilarity(string? surname1, string? surname2, Func<string, string, double> fallbackSimilarity);
}
