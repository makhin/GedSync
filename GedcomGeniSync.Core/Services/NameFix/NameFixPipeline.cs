using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services.NameFix;

/// <summary>
/// Pipeline that executes all name fix handlers in order.
/// Coordinates the chain of handlers and provides logging.
/// </summary>
public class NameFixPipeline : INameFixPipeline
{
    private readonly List<INameFixHandler> _handlers;
    private readonly ILogger<NameFixPipeline> _logger;

    public NameFixPipeline(
        IEnumerable<INameFixHandler> handlers,
        ILogger<NameFixPipeline> logger)
    {
        _handlers = handlers
            .Where(h => h.IsEnabled)
            .OrderBy(h => h.Order)
            .ToList();
        _logger = logger;

        _logger.LogDebug("NameFixPipeline initialized with {Count} handlers: {Handlers}",
            _handlers.Count,
            string.Join(", ", _handlers.Select(h => $"{h.Name}({h.Order})")));
    }

    /// <summary>
    /// Process a context through all handlers
    /// </summary>
    public void Process(NameFixContext context)
    {
        _logger.LogDebug("Processing profile {ProfileId} ({DisplayName})",
            context.ProfileId, context.DisplayName);

        var initialChanges = context.Changes.Count;

        foreach (var handler in _handlers)
        {
            try
            {
                var changesBefore = context.Changes.Count;
                handler.Handle(context);
                var changesAfter = context.Changes.Count;

                if (changesAfter > changesBefore)
                {
                    _logger.LogDebug("Handler {Handler} made {Count} change(s)",
                        handler.Name, changesAfter - changesBefore);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler {Handler} failed for profile {ProfileId}",
                    handler.Name, context.ProfileId);
                // Continue with other handlers
            }
        }

        var totalChanges = context.Changes.Count - initialChanges;
        if (totalChanges > 0)
        {
            _logger.LogInformation("Profile {ProfileId}: {Count} total change(s)",
                context.ProfileId, totalChanges);
        }
    }

    /// <summary>
    /// Process multiple contexts
    /// </summary>
    public IEnumerable<NameFixContext> ProcessMany(IEnumerable<NameFixContext> contexts)
    {
        foreach (var context in contexts)
        {
            Process(context);
            yield return context;
        }
    }

    /// <summary>
    /// Get list of registered handlers
    /// </summary>
    public IReadOnlyList<INameFixHandler> Handlers => _handlers.AsReadOnly();
}

/// <summary>
/// Interface for the name fix pipeline
/// </summary>
public interface INameFixPipeline
{
    /// <summary>
    /// Process a single context through all handlers
    /// </summary>
    void Process(NameFixContext context);

    /// <summary>
    /// Process multiple contexts
    /// </summary>
    IEnumerable<NameFixContext> ProcessMany(IEnumerable<NameFixContext> contexts);

    /// <summary>
    /// Get list of registered handlers
    /// </summary>
    IReadOnlyList<INameFixHandler> Handlers { get; }
}
