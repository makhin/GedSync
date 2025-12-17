#!/usr/bin/env dotnet script
#r "GedcomGeniSync.ApiClient/bin/Debug/net8.0/GedcomGeniSync.ApiClient.dll"

using System;
using System.Collections.Generic;
using GedcomGeniSync.ApiClient.Models;

Console.WriteLine("Testing Phase 1 Models...\n");

// Test 1: GeniDateInput
Console.WriteLine("Test 1: GeniDateInput");
var dateInput = new GeniDateInput
{
    Year = 1974,
    Month = 12,
    Day = 17
};
Console.WriteLine($"✓ GeniDateInput created: {dateInput.Year}-{dateInput.Month}-{dateInput.Day}");

// Test 2: GeniLocationInput
Console.WriteLine("\nTest 2: GeniLocationInput");
var locationInput = new GeniLocationInput
{
    PlaceName = "Moscow, Russia"
};
Console.WriteLine($"✓ GeniLocationInput created: {locationInput.PlaceName}");

// Test 3: GeniEventInput
Console.WriteLine("\nTest 3: GeniEventInput");
var eventInput = new GeniEventInput
{
    Date = dateInput,
    Location = locationInput
};
Console.WriteLine($"✓ GeniEventInput created with date and location");

// Test 4: GeniProfileUpdate with new fields
Console.WriteLine("\nTest 4: GeniProfileUpdate with new fields");
var profileUpdate = new GeniProfileUpdate
{
    FirstName = "Ivan",
    LastName = "Ivanov",
    Birth = new GeniEventInput
    {
        Date = new GeniDateInput { Year = 1950, Month = 3, Day = 15 },
        Location = new GeniLocationInput { PlaceName = "Moscow" }
    },
    Death = new GeniEventInput
    {
        Date = new GeniDateInput { Year = 2020, Month = 12, Day = 1 },
        Location = new GeniLocationInput { PlaceName = "Moscow" }
    },
    Baptism = new GeniEventInput
    {
        Date = new GeniDateInput { Year = 1950, Month = 4, Day = 1 }
    },
    Burial = new GeniEventInput
    {
        Location = new GeniLocationInput { PlaceName = "Moscow Cemetery" }
    },
    Names = new Dictionary<string, Dictionary<string, string>>
    {
        ["ru"] = new Dictionary<string, string>
        {
            ["first_name"] = "Иван",
            ["last_name"] = "Иванов"
        },
        ["en"] = new Dictionary<string, string>
        {
            ["first_name"] = "Ivan",
            ["last_name"] = "Ivanov"
        }
    },
    Nicknames = "Vanya,Johnny",
    Title = "Dr.",
    IsAlive = false,
    CauseOfDeath = "Natural causes"
};

Console.WriteLine($"✓ GeniProfileUpdate created with:");
Console.WriteLine($"  - FirstName: {profileUpdate.FirstName}");
Console.WriteLine($"  - LastName: {profileUpdate.LastName}");
Console.WriteLine($"  - Birth: {profileUpdate.Birth?.Date?.Year}-{profileUpdate.Birth?.Date?.Month}-{profileUpdate.Birth?.Date?.Day} at {profileUpdate.Birth?.Location?.PlaceName}");
Console.WriteLine($"  - Death: {profileUpdate.Death?.Date?.Year}-{profileUpdate.Death?.Date?.Month}-{profileUpdate.Death?.Date?.Day} at {profileUpdate.Death?.Location?.PlaceName}");
Console.WriteLine($"  - Baptism date: {profileUpdate.Baptism?.Date?.Year}-{profileUpdate.Baptism?.Date?.Month}-{profileUpdate.Baptism?.Date?.Day}");
Console.WriteLine($"  - Burial location: {profileUpdate.Burial?.Location?.PlaceName}");
Console.WriteLine($"  - Names locales: {string.Join(", ", profileUpdate.Names?.Keys ?? new List<string>())}");
Console.WriteLine($"  - Nicknames: {profileUpdate.Nicknames}");
Console.WriteLine($"  - Title: {profileUpdate.Title}");
Console.WriteLine($"  - IsAlive: {profileUpdate.IsAlive}");
Console.WriteLine($"  - CauseOfDeath: {profileUpdate.CauseOfDeath}");

// Test 5: Verify backward compatibility - old code should still work
Console.WriteLine("\nTest 5: Backward compatibility");
var simpleUpdate = new GeniProfileUpdate
{
    FirstName = "John",
    LastName = "Doe"
};
Console.WriteLine($"✓ Simple update (old style) still works: {simpleUpdate.FirstName} {simpleUpdate.LastName}");

Console.WriteLine("\n✅ All tests passed!");
