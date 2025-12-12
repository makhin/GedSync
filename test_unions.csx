#!/usr/bin/env dotnet-script
#r "nuget: System.Net.Http.Json, 8.0.0"

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// Read access token from file
var tokenFile = Path.Combine(Directory.GetCurrentDirectory(), "geni_token.json");
if (!File.Exists(tokenFile))
{
    Console.WriteLine($"Token file not found: {tokenFile}");
    return;
}

var tokenJson = File.ReadAllText(tokenFile);
var tokenData = JsonSerializer.Deserialize<Dictionary<string, string>>(tokenJson);
var accessToken = tokenData?["access_token"];

if (string.IsNullOrEmpty(accessToken))
{
    Console.WriteLine("Access token not found in token file");
    return;
}

Console.WriteLine($"Using access token: {accessToken.Substring(0, 10)}...");

// Union IDs from family-response.json
var unionIds = new[] { "118937408", "118937425", "118937407" };

// Create batch request URL
var baseUrl = "https://www.geni.com/api";
var idsParam = string.Join(",", unionIds);
var url = $"{baseUrl}/union?ids={idsParam}";

Console.WriteLine($"\nFetching unions batch: {url}");

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

var response = await httpClient.GetAsync(url);
Console.WriteLine($"Response status: {response.StatusCode}");

if (response.IsSuccessStatusCode)
{
    var jsonContent = await response.Content.ReadAsStringAsync();

    // Pretty print the JSON
    var jsonDoc = JsonDocument.Parse(jsonContent);
    var prettyJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });

    Console.WriteLine("\n=== UNION BATCH RESPONSE ===");
    Console.WriteLine(prettyJson);
    Console.WriteLine("=== END RESPONSE ===\n");

    // Save to file for inspection
    var outputFile = "unions-response.json";
    File.WriteAllText(outputFile, prettyJson);
    Console.WriteLine($"Response saved to: {outputFile}");
}
else
{
    var errorContent = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Error: {errorContent}");
}
