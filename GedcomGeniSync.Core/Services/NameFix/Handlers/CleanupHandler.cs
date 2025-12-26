namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler that performs cleanup operations:
/// - Removes empty fields from locales
/// - Removes locales with no fields
/// - Trims whitespace from values
/// - Removes duplicate values across locales
/// </summary>
public class CleanupHandler : NameFixHandlerBase
{
    public override string Name => "Cleanup";
    public override int Order => 100;

    public override void Handle(NameFixContext context)
    {
        // Trim whitespace from all values
        TrimValues(context);

        // Remove empty fields
        RemoveEmptyFields(context);

        // Remove empty locales
        RemoveEmptyLocales(context);

        // Deduplicate values (e.g., if en-US and en have same value)
        DeduplicateEnglishLocales(context);
    }

    private void TrimValues(NameFixContext context)
    {
        foreach (var locale in context.Names.Keys.ToList())
        {
            var fields = context.Names[locale];
            foreach (var field in fields.Keys.ToList())
            {
                var value = fields[field];
                if (value != null)
                {
                    var trimmed = value.Trim();
                    if (trimmed != value)
                    {
                        fields[field] = trimmed;
                        // Don't record trivial whitespace changes
                    }
                }
            }
        }
    }

    private void RemoveEmptyFields(NameFixContext context)
    {
        foreach (var locale in context.Names.Keys.ToList())
        {
            var fields = context.Names[locale];
            var fieldsToRemove = fields
                .Where(kvp => string.IsNullOrWhiteSpace(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var field in fieldsToRemove)
            {
                fields.Remove(field);
            }
        }
    }

    private void RemoveEmptyLocales(NameFixContext context)
    {
        var localesToRemove = context.Names
            .Where(kvp => kvp.Value.Count == 0 || kvp.Value.Values.All(string.IsNullOrWhiteSpace))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var locale in localesToRemove)
        {
            context.Names.Remove(locale);
        }
    }

    private void DeduplicateEnglishLocales(NameFixContext context)
    {
        // If both en and en-US exist with same values, prefer en-US
        if (!context.Names.TryGetValue(Locales.EnglishShort, out var enFields)) return;
        if (!context.Names.TryGetValue(Locales.PreferredEnglish, out var enUsFields)) return;

        var fieldsToRemove = new List<string>();

        foreach (var (field, value) in enFields)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Check if en-US has the same value
            if (enUsFields.TryGetValue(field, out var enUsValue) &&
                value.Equals(enUsValue, StringComparison.Ordinal))
            {
                fieldsToRemove.Add(field);
            }
        }

        foreach (var field in fieldsToRemove)
        {
            enFields.Remove(field);
        }

        // Remove en locale if now empty
        if (enFields.Count == 0 || enFields.Values.All(string.IsNullOrWhiteSpace))
        {
            context.Names.Remove(Locales.EnglishShort);
        }
    }
}
