using System;
using System.Linq;
using System.Reflection;
using Patagames.GedcomNetSdk.Records;

// Get all properties from Individual class
var individualType = typeof(Individual);
var allProps = individualType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

Console.WriteLine("=== Individual Properties related to Multimedia ===");
foreach (var prop in allProps.Where(p =>
    p.Name.Contains("Multi", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("Media", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("Object", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("File", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine($"{prop.Name} : {prop.PropertyType.Name}");
}

Console.WriteLine("\n=== All Individual Properties (sorted) ===");
foreach (var prop in allProps.OrderBy(p => p.Name))
{
    Console.WriteLine($"{prop.Name} : {prop.PropertyType.Name}");
}
