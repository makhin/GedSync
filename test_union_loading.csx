#!/usr/bin/env dotnet script
#r "nuget: Microsoft.Extensions.Logging, 9.0.0"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"
#r "./GedcomGeniSync.Core/bin/Debug/net8.0/GedcomGeniSync.Core.dll"

using GedcomGeniSync.Services;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;

// Read token from file
var tokenPath = "geni_token.json";
if (!File.Exists(tokenPath))
{
    Console.WriteLine($"Error: Token file not found at {tokenPath}");
    return;
}

var tokenJson = File.ReadAllText(tokenPath);
var tokenData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(tokenJson);
var accessToken = tokenData["access_token"].GetString();

Console.WriteLine("Token loaded successfully");

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Create HTTP client factory
var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
services.AddHttpClient();
var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

// Create API client
var profileClientLogger = loggerFactory.CreateLogger<GeniProfileClient>();
var profileClient = new GeniProfileClient(httpClientFactory, accessToken, false, profileClientLogger);

// Get profile ID (Alexandr Makhin)
var profileId = "34828568625";

Console.WriteLine($"\n=== Testing Union Loading for Profile {profileId} ===\n");

// Get immediate family
var family = await profileClient.GetImmediateFamilyAsync(profileId);

if (family?.Nodes != null)
{
    Console.WriteLine($"\nTotal nodes: {family.Nodes.Count}");

    // Find union nodes
    var unionNodes = family.Nodes.Where(n => n.Key.StartsWith("union-")).ToList();
    Console.WriteLine($"Union nodes found: {unionNodes.Count}");

    foreach (var (nodeId, node) in unionNodes)
    {
        Console.WriteLine($"\n--- Union: {nodeId} ---");
        if (node.Union != null)
        {
            Console.WriteLine($"Status: {node.Union.Status}");
            Console.WriteLine($"Marriage Date: {node.Union.MarriageDate ?? "N/A"}");
            Console.WriteLine($"Marriage Place: {node.Union.MarriagePlace ?? "N/A"}");
            Console.WriteLine($"Divorce Date: {node.Union.Divorce?.Date?.FormattedDate ?? "N/A"}");
            Console.WriteLine($"Divorce Place: {node.Union.Divorce?.Location?.FormattedLocation ?? "N/A"}");
            Console.WriteLine($"Partners: {node.Union.Partners?.Count ?? 0}");
            Console.WriteLine($"Children: {node.Union.Children?.Count ?? 0}");
        }
        else
        {
            Console.WriteLine("ERROR: Union data is NULL!");
        }
    }
}
else
{
    Console.WriteLine("Failed to get immediate family");
}

Console.WriteLine("\n=== Test Complete ===\n");
