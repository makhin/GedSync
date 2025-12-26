namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Interface for name fix handlers in the processing pipeline.
/// Each handler performs a specific type of name correction.
/// </summary>
public interface INameFixHandler
{
    /// <summary>
    /// Unique name of this handler (used in logging)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Order in the pipeline. Lower values run first.
    /// Suggested ranges:
    /// - 10-19: Script splitting (separate mixed scripts)
    /// - 20-29: Script moving (move Cyrillic to ru, etc.)
    /// - 30-39: Transliteration (create Latin from Cyrillic)
    /// - 40-49: Gender-specific fixes (feminine surnames)
    /// - 50-79: Language-specific fixes (Lithuanian, Estonian, etc.)
    /// - 90-99: Cleanup (remove empty fields)
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Process the context. Modify Names dictionary and record changes.
    /// </summary>
    /// <param name="context">The context containing profile data and change tracking</param>
    void Handle(NameFixContext context);

    /// <summary>
    /// Whether this handler is enabled. Default is true.
    /// </summary>
    bool IsEnabled => true;
}

/// <summary>
/// Base class for name fix handlers with common functionality
/// </summary>
public abstract class NameFixHandlerBase : INameFixHandler
{
    public abstract string Name { get; }
    public abstract int Order { get; }

    public virtual bool IsEnabled => true;

    public abstract void Handle(NameFixContext context);

    /// <summary>
    /// Helper method to record a change with this handler's name
    /// </summary>
    protected void SetName(NameFixContext context, string locale, string field, string? value, string reason)
    {
        context.SetName(locale, field, value, reason, Name);
    }

    /// <summary>
    /// Helper method to move a name with this handler's name
    /// </summary>
    protected void MoveName(NameFixContext context, string fromLocale, string toLocale, string field, string reason)
    {
        context.MoveName(fromLocale, toLocale, field, reason, Name);
    }

    /// <summary>
    /// Primary field definitions for iteration.
    /// </summary>
    protected static readonly (string FieldName, Func<NameFixContext, string?> Getter, Action<NameFixContext, string?> Setter)[] PrimaryFields =
    {
        ("FirstName", c => c.FirstName, (c, v) => c.FirstName = v),
        ("LastName", c => c.LastName, (c, v) => c.LastName = v),
        ("MiddleName", c => c.MiddleName, (c, v) => c.MiddleName = v),
        ("MaidenName", c => c.MaidenName, (c, v) => c.MaidenName = v),
    };

    /// <summary>
    /// Iterate over primary fields with a processor function.
    /// </summary>
    protected void ForEachPrimaryField(
        NameFixContext context,
        Func<string, string> processor,
        string reason)
    {
        foreach (var (fieldName, getter, setter) in PrimaryFields)
        {
            var value = getter(context);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var newValue = processor(value);
            if (newValue == value) continue;

            context.Changes.Add(new NameChange
            {
                Field = fieldName,
                OldValue = value,
                NewValue = newValue,
                Reason = reason,
                Handler = Name
            });
            setter(context, newValue);
        }
    }

    /// <summary>
    /// Iterate over all locale fields with a processor function.
    /// </summary>
    protected void ForEachLocaleField(
        NameFixContext context,
        Func<string, string> processor,
        string reason)
    {
        foreach (var locale in context.Names.Keys.ToList())
        {
            var fields = context.GetLocaleFields(locale);
            if (fields == null) continue;

            foreach (var field in NameFields.All)
            {
                if (!fields.TryGetValue(field, out var value)) continue;
                if (string.IsNullOrWhiteSpace(value)) continue;

                var newValue = processor(value);
                if (newValue != value)
                {
                    SetName(context, locale, field, newValue, reason);
                }
            }
        }
    }
}
