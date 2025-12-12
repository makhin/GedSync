#!/usr/bin/env dotnet-script
#r "nuget: FuzzySharp, 2.0.2"

using System;
using System.Collections.Immutable;
using FuzzySharp;

// Simulate the matching logic
var sourceName = "Махин"; // GEDCOM LastName
var targetName = (string?)null; // Geni LastName (empty)
var targetMaidenName = "Махин"; // Geni MaidenName

Console.WriteLine($"Source LastName: '{sourceName}'");
Console.WriteLine($"Target LastName: '{targetName ?? "(null)"}'");
Console.WriteLine($"Target MaidenName: '{targetMaidenName}'");
Console.WriteLine();

// OLD LOGIC (before fix):
Console.WriteLine("=== OLD LOGIC ===");
if (string.IsNullOrEmpty(targetName))
{
    Console.WriteLine("Early exit with score 0.3 - PROBLEM!");
    Console.WriteLine("Match would fail because of early exit before MaidenName check");
}

// NEW LOGIC (after fix):
Console.WriteLine("\n=== NEW LOGIC ===");
if (string.IsNullOrEmpty(targetName) && !string.IsNullOrEmpty(targetMaidenName) && !string.IsNullOrEmpty(sourceName))
{
    var normalizedMaiden = targetMaidenName.ToLowerInvariant();
    var sourceNorm = sourceName.ToLowerInvariant();

    Console.WriteLine($"Comparing MaidenName '{normalizedMaiden}' with source LastName '{sourceNorm}'");

    if (normalizedMaiden == sourceNorm)
    {
        Console.WriteLine("✓ EXACT MATCH via MaidenName! Score = 1.0");
    }
    else
    {
        Console.WriteLine("✗ No match");
    }
}

Console.WriteLine("\n=== EXPECTED RESULT ===");
Console.WriteLine("Владимир Витальевич Махин (GEDCOM) should match Владимир Витальевич (Geni)");
Console.WriteLine("Because GEDCOM.LastName='Махин' matches Geni.MaidenName='Махин'");
Console.WriteLine("With FirstName and BirthDate match, total score should exceed 70% threshold");
