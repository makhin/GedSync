using System.Diagnostics.CodeAnalysis;

namespace GedcomGeniSync.Services;

/// <summary>
/// Manages synchronization state including ID mappings and processed records
/// </summary>
[ExcludeFromCodeCoverage]
public class SyncStateManager : ISyncStateManager
{
    private readonly Dictionary<string, string> _gedcomToGeniMap = new();
    private readonly Dictionary<string, string> _geniToGedcomMap = new();
    private readonly HashSet<string> _processedGedcomIds = new();

    /// <inheritdoc />
    public bool IsProcessed(string gedcomId)
    {
        return _processedGedcomIds.Contains(gedcomId);
    }

    /// <inheritdoc />
    public void MarkAsProcessed(string gedcomId)
    {
        _processedGedcomIds.Add(gedcomId);
    }

    /// <inheritdoc />
    public void AddMapping(string gedcomId, string geniId)
    {
        _gedcomToGeniMap[gedcomId] = geniId;
        _geniToGedcomMap[geniId] = gedcomId;
    }

    /// <inheritdoc />
    public string? GetGeniId(string gedcomId)
    {
        return _gedcomToGeniMap.GetValueOrDefault(gedcomId);
    }

    /// <inheritdoc />
    public string? GetGedcomId(string geniId)
    {
        return _geniToGedcomMap.GetValueOrDefault(geniId);
    }

    /// <inheritdoc />
    public bool IsMappedToGeni(string geniId)
    {
        return _geniToGedcomMap.ContainsKey(geniId);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAllMappings()
    {
        return _gedcomToGeniMap;
    }

    /// <inheritdoc />
    public void LoadState(Dictionary<string, string> gedcomToGeniMap, List<string> processedIds)
    {
        foreach (var (gedId, geniId) in gedcomToGeniMap)
        {
            _gedcomToGeniMap[gedId] = geniId;
            _geniToGedcomMap[geniId] = gedId;
        }

        foreach (var id in processedIds)
        {
            _processedGedcomIds.Add(id);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetProcessedIds()
    {
        return _processedGedcomIds;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _gedcomToGeniMap.Clear();
        _geniToGedcomMap.Clear();
        _processedGedcomIds.Clear();
    }
}
