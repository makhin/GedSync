using System.Diagnostics.CodeAnalysis;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services;

[ExcludeFromCodeCoverage]
public class SyncState
{
    public Dictionary<string, string> GedcomToGeniMap { get; set; } = new();
    public List<string> ProcessedIds { get; set; } = new();
    public List<SyncResult> Results { get; set; } = new();
}
