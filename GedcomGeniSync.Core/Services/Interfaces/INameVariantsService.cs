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
}
