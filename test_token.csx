using System.Net.Http;
using System.Net.Http.Headers;

var token = "no3gDiAFWECxzm0NfJxYNDQg8rWbIkDCw5za8WGf";
var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

// Test getting current user profile
var response = await client.GetAsync("https://www.geni.com/api/profile");
Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine($"Response: {await response.Content.ReadAsStringAsync()}");
