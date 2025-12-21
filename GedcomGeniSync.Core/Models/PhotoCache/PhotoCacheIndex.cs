namespace GedcomGeniSync.Models;

public class PhotoCacheIndex
{
    public int Version { get; set; } = 1;
    public Dictionary<string, PhotoCacheEntry> Entries { get; set; } = new();
}
