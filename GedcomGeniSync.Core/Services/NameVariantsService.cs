using System.Collections.Generic;
using System.IO;
using System.Linq;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Service for name variants lookup and transliteration
/// </summary>
public class NameVariantsService : INameVariantsService
{
    private readonly Dictionary<string, HashSet<string>> _givenNameGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _surnameGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<NameVariantsService> _logger;

    public NameVariantsService(ILogger<NameVariantsService> logger)
    {
        _logger = logger;
        LoadBuiltInVariants();
    }

    /// <summary>
    /// Load CSV files with name variants
    /// </summary>
    public void LoadFromCsv(string givenNamesPath, string surnamesPath)
    {
        if (File.Exists(givenNamesPath))
        {
            LoadGivenNamesCsv(givenNamesPath);
        }

        if (File.Exists(surnamesPath))
        {
            LoadSurnamesCsv(surnamesPath);
        }
    }

    private void LoadGivenNamesCsv(string path)
    {
        _logger.LogInformation("Loading given names from {Path}", path);

        var lines = File.ReadAllLines(path);
        var count = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim().Trim('"');
                var variants = parts[1].Trim().Trim('"')
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();

                AddGivenNameVariants(name, variants);
                count++;
            }
        }

        _logger.LogInformation("Loaded {Count} given name entries", count);
    }

    private void LoadSurnamesCsv(string path)
    {
        _logger.LogInformation("Loading surnames from {Path}", path);

        var lines = File.ReadAllLines(path);
        var count = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim().Trim('"');
                var variants = parts[1].Trim().Trim('"')
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();

                AddSurnameVariants(name, variants);
                count++;
            }
        }

        _logger.LogInformation("Loaded {Count} surname entries", count);
    }

    /// <summary>
    /// Check if two given names are equivalent
    /// </summary>
    public bool AreEquivalent(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return false;

        var norm1 = name1.ToLowerInvariant().Trim();
        var norm2 = name2.ToLowerInvariant().Trim();

        if (norm1 == norm2)
            return true;

        // Check if in same group
        if (_givenNameGroups.TryGetValue(norm1, out var group1))
        {
            if (group1.Contains(norm2))
                return true;
        }

        if (_givenNameGroups.TryGetValue(norm2, out var group2))
        {
            if (group2.Contains(norm1))
                return true;
        }

        // Check transliterated versions
        var translit1 = Transliterate(norm1);
        var translit2 = Transliterate(norm2);

        if (translit1 == translit2)
            return true;

        if (_givenNameGroups.TryGetValue(translit1, out var group3))
        {
            if (group3.Contains(translit2))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if two surnames are equivalent
    /// </summary>
    public bool AreEquivalentSurnames(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return false;

        var norm1 = name1.ToLowerInvariant().Trim();
        var norm2 = name2.ToLowerInvariant().Trim();

        if (norm1 == norm2)
            return true;

        // Check if in same group
        if (_surnameGroups.TryGetValue(norm1, out var group1))
        {
            if (group1.Contains(norm2))
                return true;
        }

        if (_surnameGroups.TryGetValue(norm2, out var group2))
        {
            if (group2.Contains(norm1))
                return true;
        }

        // Check transliterated versions
        var translit1 = Transliterate(norm1);
        var translit2 = Transliterate(norm2);

        return translit1 == translit2;
    }

    /// <summary>
    /// Transliterate text from Cyrillic to Latin
    /// </summary>
    public string Transliterate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (var c in text)
        {
            if (TransliterationConstants.CyrillicToLatin.TryGetValue(c, out var replacement))
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Add custom given name variants
    /// </summary>
    public void AddGivenNameVariants(string baseName, IEnumerable<string> variants)
    {
        var key = baseName.ToLowerInvariant();

        if (!_givenNameGroups.ContainsKey(key))
        {
            _givenNameGroups[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var variant in variants)
        {
            var variantKey = variant.ToLowerInvariant();
            _givenNameGroups[key].Add(variantKey);

            // Also add reverse mapping
            if (!_givenNameGroups.ContainsKey(variantKey))
            {
                _givenNameGroups[variantKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            _givenNameGroups[variantKey].Add(key);
        }
    }

    /// <summary>
    /// Add custom surname variants
    /// </summary>
    public void AddSurnameVariants(string baseName, IEnumerable<string> variants)
    {
        var key = baseName.ToLowerInvariant();

        if (!_surnameGroups.ContainsKey(key))
        {
            _surnameGroups[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var variant in variants)
        {
            var variantKey = variant.ToLowerInvariant();
            _surnameGroups[key].Add(variantKey);

            // Also add reverse mapping
            if (!_surnameGroups.ContainsKey(variantKey))
            {
                _surnameGroups[variantKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            _surnameGroups[variantKey].Add(key);
        }
    }

    /// <summary>
    /// Load built-in common variants (Slavic names focus)
    /// </summary>
    private void LoadBuiltInVariants()
    {
        // Common Russian/Ukrainian/Polish given name equivalents
        var givenNameEquivalents = new Dictionary<string, string[]>
        {
            // Male names
            ["иван"] = new[] { "ivan", "john", "johann", "jan", "jean", "giovanni", "juan", "ioan" },
            ["александр"] = new[] { "alexander", "alex", "oleksandr", "aleksander", "саша", "sasha" },
            ["михаил"] = new[] { "michael", "michel", "miguel", "mykhailo", "michal", "миша" },
            ["николай"] = new[] { "nicholas", "nicolas", "mykola", "mikolaj", "коля" },
            ["пётр"] = new[] { "peter", "pierre", "pedro", "petro", "piotr" },
            ["павел"] = new[] { "paul", "pavel", "pawel", "pablo", "паша" },
            ["андрей"] = new[] { "andrew", "andrei", "andriy", "andrzej", "andre" },
            ["сергей"] = new[] { "sergei", "serge", "sergiy", "серёжа" },
            ["дмитрий"] = new[] { "dmitry", "dmitri", "dmytro", "дима" },
            ["владимир"] = new[] { "vladimir", "volodymyr", "wladimir", "володя" },
            ["борис"] = new[] { "boris", "borys" },
            ["григорий"] = new[] { "gregory", "grigory", "hryhoriy", "гриша" },
            ["василий"] = new[] { "vasily", "basil", "vasyl", "вася" },
            ["яков"] = new[] { "jacob", "james", "jakub", "yakov" },
            ["семён"] = new[] { "simon", "semen", "семен" },
            ["фёдор"] = new[] { "theodore", "fedor", "федор", "федя" },

            // Female names
            ["мария"] = new[] { "maria", "mary", "marie", "марія", "маша" },
            ["анна"] = new[] { "anna", "anne", "ann", "hanna", "ганна", "аня" },
            ["елена"] = new[] { "helen", "helena", "elena", "olena", "лена" },
            ["екатерина"] = new[] { "catherine", "katarina", "kateryna", "катя" },
            ["наталья"] = new[] { "natalia", "natalie", "nataliya", "наташа" },
            ["ольга"] = new[] { "olga", "olha", "helga" },
            ["татьяна"] = new[] { "tatiana", "tanya", "tetiana", "таня" },
            ["ирина"] = new[] { "irina", "irene", "iryna" },
            ["светлана"] = new[] { "svetlana", "svitlana", "света" },
            ["людмила"] = new[] { "ludmila", "lyudmila", "liudmyla", "люда" },
            ["евгения"] = new[] { "eugenia", "yevheniya", "женя" },
            ["софья"] = new[] { "sophia", "sofia", "zofia", "софія", "соня" },
            ["елизавета"] = new[] { "elizabeth", "yelyzaveta", "elzbieta", "лиза" },
            ["валентина"] = new[] { "valentina", "валя" },
            ["галина"] = new[] { "galina", "halyna", "галя" }
        };

        foreach (var (key, variants) in givenNameEquivalents)
        {
            AddGivenNameVariants(key, variants);
        }

        _logger.LogInformation("Loaded {Count} built-in given name groups", givenNameEquivalents.Count);
    }
}
