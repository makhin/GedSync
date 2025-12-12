// Test CSV parsing logic
var line = "\"john\",\"ean eoin evan evans gianni giovanni gohn hank\"";
Console.WriteLine($"Original line: {line}");

var parts = line.Split(',');
Console.WriteLine($"\nParts count: {parts.Length}");

for (int i = 0; i < parts.Length; i++)
{
    Console.WriteLine($"Part {i}: [{parts[i]}]");
}

var name = parts[0].Trim().Trim('"');
Console.WriteLine($"\nName: [{name}]");

var variantsString = parts[1].Trim().Trim('"');
Console.WriteLine($"Variants string: [{variantsString}]");

var variants = variantsString.Split('|')
    .Select(v => v.Trim())
    .Where(v => !string.IsNullOrEmpty(v))
    .ToList();

Console.WriteLine($"\nVariants count with | split: {variants.Count}");
foreach (var v in variants.Take(10))
{
    Console.WriteLine($"  [{v}]");
}

// What it SHOULD be with space split:
var variantsCorrect = variantsString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
    .Select(v => v.Trim())
    .ToList();

Console.WriteLine($"\nVariants count with SPACE split: {variantsCorrect.Count}");
foreach (var v in variantsCorrect.Take(10))
{
    Console.WriteLine($"  [{v}]");
}
