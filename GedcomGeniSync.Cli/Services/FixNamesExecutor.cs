using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Cli.Models;
using GedcomGeniSync.Services.NameFix;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Executor for the fix-names command.
/// Performs BFS traversal of the Geni tree and applies name fixes.
/// </summary>
public class FixNamesExecutor
{
    private readonly IGeniProfileClient _profileClient;
    private readonly INameFixPipeline _pipeline;
    private readonly ILogger _logger;

    private readonly bool _dryRun;
    private readonly int? _maxDepth;
    private readonly string _progressFile;
    private readonly string _logFile;

    private FixNamesProgress _progress;
    private readonly Queue<(string ProfileId, int Depth)> _queue = new();

    // Statistics
    private int _profilesVisited;
    private int _profilesChanged;
    private int _profilesFailed;

    public FixNamesExecutor(
        IGeniProfileClient profileClient,
        INameFixPipeline pipeline,
        ILogger logger,
        bool dryRun,
        int? maxDepth,
        string progressFile,
        string logFile)
    {
        _profileClient = profileClient;
        _pipeline = pipeline;
        _logger = logger;
        _dryRun = dryRun;
        _maxDepth = maxDepth;
        _progressFile = progressFile;
        _logFile = logFile;
        _progress = new FixNamesProgress();
    }

    /// <summary>
    /// Execute the fix-names process
    /// </summary>
    public async Task<FixNamesResult> ExecuteAsync(
        string anchorProfileId,
        bool resume,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting fix-names from anchor: {AnchorId}", anchorProfileId);
        _logger.LogInformation("Dry run: {DryRun}", _dryRun);
        _logger.LogInformation("Max depth: {MaxDepth}", _maxDepth?.ToString() ?? "unlimited");

        // Load or create progress
        if (resume && File.Exists(_progressFile))
        {
            await LoadProgressAsync();
            _logger.LogInformation("Resumed from progress file. Already processed: {Count} profiles",
                _progress.ProcessedProfiles.Count);

            // Restore queue
            foreach (var entry in _progress.QueueState)
            {
                _queue.Enqueue((entry.ProfileId, entry.Depth));
            }
        }
        else
        {
            _progress = new FixNamesProgress
            {
                AnchorProfile = anchorProfileId,
                MaxDepth = _maxDepth
            };
            _queue.Enqueue((anchorProfileId, 0));
        }

        // Process queue
        try
        {
            await ProcessQueueAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Operation cancelled. Saving progress...");
        }
        finally
        {
            // Save progress
            await SaveProgressAsync();
        }

        return new FixNamesResult
        {
            ProfilesVisited = _profilesVisited,
            ProfilesChanged = _profilesChanged,
            ProfilesFailed = _profilesFailed,
            TotalChanges = _progress.TotalChanges,
            WasInterrupted = cancellationToken.IsCancellationRequested
        };
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (_queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (profileId, depth) = _queue.Dequeue();

            // Check depth limit
            if (_maxDepth.HasValue && depth > _maxDepth.Value)
            {
                _logger.LogDebug("Skipping {ProfileId}: exceeds max depth {MaxDepth}",
                    profileId, _maxDepth.Value);
                continue;
            }

            // Check if already processed
            if (_progress.IsExpanded(profileId))
            {
                _logger.LogDebug("Skipping {ProfileId}: already processed", profileId);
                continue;
            }

            // Process this profile
            await ProcessProfileAsync(profileId, depth, cancellationToken);

            // Save progress periodically
            if (_profilesVisited % 10 == 0)
            {
                await SaveProgressAsync();
            }
        }
    }

    private async Task ProcessProfileAsync(string profileId, int depth, CancellationToken cancellationToken)
    {
        _profilesVisited++;

        try
        {
            _logger.LogInformation("[{Visited}] Processing {ProfileId} at depth {Depth}...",
                _profilesVisited, profileId, depth);

            // Get immediate family (includes the profile itself)
            var family = await _profileClient.GetImmediateFamilyAsync(profileId);
            if (family?.Nodes == null)
            {
                _logger.LogWarning("Failed to get family for {ProfileId}", profileId);
                _progress.MarkFailed(profileId);
                _profilesFailed++;
                return;
            }

            // Process all profile nodes
            foreach (var (nodeId, node) in family.Nodes)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Skip union nodes
                if (nodeId.StartsWith("union-")) continue;

                // Skip already processed
                if (_progress.IsNameProcessed(nodeId)) continue;

                // Process this node's names
                var hadChanges = await ProcessNodeAsync(node);

                // Mark as processed
                _progress.MarkNameProcessed(nodeId, hadChanges);

                if (hadChanges)
                {
                    _profilesChanged++;
                }

                // Add to queue for BFS (if not the focus profile)
                if (nodeId != $"profile-{profileId}"
                    && nodeId != profileId
                    && !_progress.IsExpanded(nodeId))
                {
                    _queue.Enqueue((nodeId, depth + 1));
                }
            }

            _progress.MarkExpanded(profileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing profile {ProfileId}", profileId);
            _progress.MarkFailed(profileId);
            _profilesFailed++;
        }
    }

    private async Task<bool> ProcessNodeAsync(GeniNode node)
    {
        if (node.Id == null) return false;

        // Create context from node
        var context = NameFixContext.FromGeniNode(node);

        // Run through pipeline
        _pipeline.Process(context);

        // Check if any changes were made
        if (!context.IsDirty)
        {
            _logger.LogDebug("No changes needed for {ProfileId}", node.Id);
            return false;
        }

        // Log changes
        _logger.LogInformation("Profile {ProfileId} ({DisplayName}): {ChangeCount} changes",
            node.Id, context.DisplayName, context.Changes.Count);

        foreach (var change in context.Changes)
        {
            _logger.LogInformation("  - {Change}", change);
        }

        // Add to progress log
        _progress.AddChange(ProfileChangeLog.FromContext(context));

        // Write to log file
        await AppendToLogFileAsync(context);

        // Apply changes if not dry run
        if (!_dryRun)
        {
            var update = context.ToProfileUpdate();
            var updated = await _profileClient.UpdateProfileAsync(node.Id, update);

            if (updated == null)
            {
                _logger.LogWarning("Failed to update profile {ProfileId}", node.Id);
                return false;
            }

            _logger.LogInformation("Successfully updated profile {ProfileId}", node.Id);
        }
        else
        {
            _logger.LogInformation("[DRY-RUN] Would update profile {ProfileId}", node.Id);
        }

        return true;
    }

    private async Task LoadProgressAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_progressFile);
            _progress = JsonSerializer.Deserialize<FixNamesProgress>(json) ?? new FixNamesProgress();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load progress file, starting fresh");
            _progress = new FixNamesProgress();
        }
    }

    private async Task SaveProgressAsync()
    {
        try
        {
            // Save queue state
            _progress.QueueState = _queue.Select(q => new QueueEntry
            {
                ProfileId = q.ProfileId,
                Depth = q.Depth
            }).ToList();

            _progress.LastUpdated = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            });

            await File.WriteAllTextAsync(_progressFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save progress file");
        }
    }

    private async Task AppendToLogFileAsync(NameFixContext context)
    {
        try
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                profile_id = context.ProfileId,
                profile_url = context.ProfileUrl,
                display_name = context.DisplayName,
                changes = context.Changes.Select(c => new
                {
                    field = c.Field,
                    from_locale = c.FromLocale,
                    to_locale = c.ToLocale,
                    old_value = c.OldValue,
                    new_value = c.NewValue,
                    reason = c.Reason,
                    handler = c.Handler
                })
            };

            var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            });
            await File.AppendAllTextAsync(_logFile, json + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append to log file");
        }
    }
}

/// <summary>
/// Result of fix-names execution
/// </summary>
public class FixNamesResult
{
    public int ProfilesVisited { get; set; }
    public int ProfilesChanged { get; set; }
    public int ProfilesFailed { get; set; }
    public int TotalChanges { get; set; }
    public bool WasInterrupted { get; set; }
}
