using System.Collections.Generic;

namespace GedcomGeniSync.Services.Interfaces;

public interface INameVariantsService
{
    void LoadFromCsv(string givenNamesPath, string surnamesPath);
    bool AreEquivalent(string name1, string name2);
    bool AreEquivalentSurnames(string name1, string name2);
    string Transliterate(string text);
    void AddGivenNameVariants(string baseName, IEnumerable<string> variants);
    void AddSurnameVariants(string baseName, IEnumerable<string> variants);

    /// <summary>
    /// Find the canonical form for a given name variant.
    /// Returns null if the name is not found in the dictionary.
    /// </summary>
    string? FindCanonicalGivenName(string name);

    /// <summary>
    /// Find the canonical form for a surname variant.
    /// Returns null if the name is not found in the dictionary.
    /// </summary>
    string? FindCanonicalSurname(string name);

    /// <summary>
    /// Check if a given name exists in the dictionary (either as canonical or variant).
    /// </summary>
    bool IsKnownGivenName(string name);

    /// <summary>
    /// Check if a surname exists in the dictionary (either as canonical or variant).
    /// </summary>
    bool IsKnownSurname(string name);
}
