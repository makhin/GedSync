#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Extensions.Logging, 8.0.0"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.0"

using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

Console.WriteLine("Testing CSV loading with real files...\n");

// Test with the actual givenname_similar_names.csv
var givenNamesPath = "givenname_similar_names.csv";
if (File.Exists(givenNamesPath))
{
    var lines = File.ReadAllLines(givenNamesPath).Take(5).ToArray();

    Console.WriteLine($"First 5 entries from {givenNamesPath}:\n");

    foreach (var line in lines)
    {
        var parts = line.Split(',');
        if (parts.Length >= 2)
        {
            var name = parts[0].Trim().Trim('"');

            // Count variants with space split (CORRECT way)
            var variantsCorrect = parts[1].Trim().Trim('"')
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            Console.WriteLine($"Name: {name}");
            Console.WriteLine($"  Variants loaded: {variantsCorrect.Count}");
            Console.WriteLine($"  Sample variants: {string.Join(", ", variantsCorrect.Take(10))}");
            Console.WriteLine();
        }
    }

    Console.WriteLine($"\nTotal lines in file: {File.ReadAllLines(givenNamesPath).Length}");
}
else
{
    Console.WriteLine($"File {givenNamesPath} not found!");
}
