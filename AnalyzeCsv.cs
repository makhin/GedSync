using System;
using System.IO;
using System.Linq;

class Program
{
    static void Main()
    {
        AnalyzeCsv("givenname_similar_names.csv", "Given Names");
        AnalyzeCsv("surname_similar_names.csv", "Surnames");
    }

    static void AnalyzeCsv(string path, string type)
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
        Console.WriteLine($"Total lines: {lines.Length:N0}");

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

        Console.WriteLine($"Base names: {totalNames:N0}");
        Console.WriteLine($"Total variants: {totalVariants:N0}");
        Console.WriteLine($"Avg variants/name: {(double)totalVariants / totalNames:F1}");
        Console.WriteLine($"Max variants: '{maxVariantsName}' ({maxVariants})");

        Console.WriteLine("\nFirst 3 examples:");
        foreach (var line in lines.Take(3))
        {
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim().Trim('"');
                var variantCount = parts[1].Trim().Trim('"')
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Length;
                var sampleVariants = parts[1].Trim().Trim('"')
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Take(8);

                Console.WriteLine($"  '{name}': {variantCount} variants ({string.Join(", ", sampleVariants)}...)");
            }
        }
    }
}
