namespace GedcomGeniSync.Models;

public record PhotoCompareResult
{
    public required string SourceUrl { get; init; }
    public required string DestinationUrl { get; init; }
    public double Similarity { get; init; }
    public bool IsMatch { get; init; }
    public string? Reason { get; init; }
}
