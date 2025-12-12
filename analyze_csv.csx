using System;
using System.IO;
using System.Linq;

void AnalyzeCsv(string path, string type)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"File {path} not found!");
        return;
    }

    var lines = File.ReadAllLines(path);
    var totalNames = 0;
    var totalVariants = 0;
    var maxVariants = 0;
    var maxVariantsName = "";

    Console.WriteLine($"\n=== Analyzing {type}: {path} ===");
    Console.WriteLine($"Total lines (names): {lines.Length:N0}");
    Console.WriteLine();

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

            totalNames++;
            totalVariants += variants.Count;

            if (variants.Count > maxVariants)
            {
                maxVariants = variants.Count;
                maxVariantsName = name;
            }
        }
    }

    Console.WriteLine($"Total base names: {totalNames:N0}");
    Console.WriteLine($"Total variants across all names: {totalVariants:N0}");
    Console.WriteLine($"Average variants per name: {(double)totalVariants / totalNames:F1}");
    Console.WriteLine($"Name with most variants: '{maxVariantsName}' ({maxVariants} variants)");

    // Show first 5 examples
    Console.WriteLine("\nFirst 5 examples:");
    foreach (var line in lines.Take(5))
    {
        var parts = line.Split(',');
        if (parts.Length >= 2)
        {
            var name = parts[0].Trim().Trim('"');
            var variants = parts[1].Trim().Trim('"')
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(10)
                .ToList();

            Console.WriteLine($"  '{name}' -> {variants.Count}+ variants: {string.Join(", ", variants)}...");
        }
    }
}

AnalyzeCsv("givenname_similar_names.csv", "Given Names");
AnalyzeCsv("surname_similar_names.csv", "Surnames");
