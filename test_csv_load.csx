using System;
using System.IO;
using System.Linq;

// Simulate CSV parsing
var givenNamesPath = "givenname_similar_names.csv";
var lines = File.ReadAllLines(givenNamesPath);

Console.WriteLine($"Total lines: {lines.Length}");
Console.WriteLine($"First line (header): {lines[0]}");
Console.WriteLine();

var testLines = lines.Skip(1).Take(5);
foreach (var line in testLines)
{
    var parts = line.Split(',');
    if (parts.Length >= 2)
    {
        var name = parts[0].Trim().Trim('"');
        
        // OLD WAY (with pipe split)
        var variantsOld = parts[1].Trim().Trim('"')
            .Split('|')
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();
            
        // NEW WAY (with space split)
        var variantsNew = parts[1].Trim().Trim('"')
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();
            
        Console.WriteLine($"Name: {name}");
        Console.WriteLine($"  Old way (pipe split): {variantsOld.Count} variants");
        if (variantsOld.Count > 0)
            Console.WriteLine($"    First variant: '{variantsOld[0].Substring(0, Math.Min(50, variantsOld[0].Length))}...'");
        Console.WriteLine($"  New way (space split): {variantsNew.Count} variants");
        Console.WriteLine($"    First 5: {string.Join(", ", variantsNew.Take(5))}");
        Console.WriteLine();
    }
}
