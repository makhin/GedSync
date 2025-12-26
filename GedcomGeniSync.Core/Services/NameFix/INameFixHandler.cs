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
}
