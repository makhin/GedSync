using Patagames.GedcomNetSdk;
using Patagames.GedcomNetSdk.Records;

// Activate SDK
try { Activate.ForPersonalUse("test@example.com"); } catch { }

// Parse a small GEDCOM snippet
var parser = new Parser(@"c:\code\GedSync\geni.ged");
var transmission = new GedcomTransmission();
transmission.Deserialize(parser);

// Get first individual
var individual = transmission.Records.OfType<Individual>().FirstOrDefault();
if (individual != null)
{
    Console.WriteLine("Individual properties:");
    var props = individual.GetType().GetProperties();
    foreach (var prop in props)
    {
        Console.WriteLine($"  {prop.Name}: {prop.PropertyType.Name}");
    }
}
