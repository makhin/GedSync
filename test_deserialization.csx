#!/usr/bin/env dotnet-script
#r "nuget: System.Text.Json, 8.0.0"

using System;
using System.IO;
using System.Text.Json;

var json = File.ReadAllText("family-response.json");
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

// Parse as generic JSON first
var document = JsonDocument.Parse(json);
var results = document.RootElement.GetProperty("results");

Console.WriteLine($"Found {results.GetArrayLength()} profiles");
Console.WriteLine();

foreach (var profile in results.EnumerateArray())
{
    var id = profile.GetProperty("id").GetString();
    var name = profile.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "(no name)";
    var firstName = profile.TryGetProperty("first_name", out var fnEl) ? fnEl.GetString() : "(null)";
    var lastName = profile.TryGetProperty("last_name", out var lnEl) ? lnEl.GetString() : "(null)";
    var gender = profile.TryGetProperty("gender", out var gEl) ? gEl.GetString() : "(null)";

    Console.WriteLine($"Profile: {id}");
    Console.WriteLine($"  Name: {name}");
    Console.WriteLine($"  FirstName: {firstName}");
    Console.WriteLine($"  LastName: {lastName}");
    Console.WriteLine($"  Gender: {gender}");

    // Check birth
    if (profile.TryGetProperty("birth", out var birth))
    {
        Console.WriteLine("  Birth object found:");
        if (birth.TryGetProperty("date", out var birthDate))
        {
            var formattedDate = birthDate.TryGetProperty("formatted_date", out var fd) ? fd.GetString() : "(null)";
            Console.WriteLine($"    Date: {formattedDate}");
        }
        if (birth.TryGetProperty("location", out var birthLoc))
        {
            var placeName = birthLoc.TryGetProperty("place_name", out var pn) ? pn.GetString() : "(null)";
            Console.WriteLine($"    Place: {placeName}");
        }
    }

    // Check names (multilingual)
    if (profile.TryGetProperty("names", out var names))
    {
        Console.WriteLine("  Multilingual names:");
        foreach (var lang in names.EnumerateObject())
        {
            Console.WriteLine($"    {lang.Name}:");
            foreach (var field in lang.Value.EnumerateObject())
            {
                Console.WriteLine($"      {field.Name}: {field.Value.GetString()}");
            }
        }
    }

    Console.WriteLine();
}
