using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Service for interactive user confirmation before profile operations
/// </summary>
public class InteractiveConfirmationService
{
    private readonly ILogger _logger;
    private readonly bool _isEnabled;

    public InteractiveConfirmationService(bool isEnabled, ILogger<InteractiveConfirmationService> logger)
    {
        _isEnabled = isEnabled;
        _logger = logger;
    }

    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Confirms profile addition with user
    /// </summary>
    public ConfirmationResult ConfirmAddProfile(
        string sourceId,
        PersonData personData,
        string relationType,
        RelativeInfo primaryRelative,
        List<RelativeInfo>? additionalRelatives = null)
    {
        if (!_isEnabled)
            return ConfirmationResult.Approved;

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("ADD PROFILE - CONFIRMATION REQUIRED");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"Source ID:        {sourceId}");
        Console.WriteLine($"Name:             {FormatPersonName(personData.FirstName, personData.MiddleName, personData.LastName, personData.MaidenName)}");
        Console.WriteLine($"Gender:           {personData.Gender ?? "Unknown"}");
        Console.WriteLine($"Birth:            {FormatLifeEvent(personData.BirthDate, personData.BirthPlace)}");
        if (!string.IsNullOrEmpty(personData.DeathDate))
            Console.WriteLine($"Death:            {FormatLifeEvent(personData.DeathDate, personData.DeathPlace)}");
        Console.WriteLine();
        Console.WriteLine("RELATIONSHIPS:");
        Console.WriteLine($"  Primary:        {relationType} of {primaryRelative.Name}");
        Console.WriteLine($"                  (Source: {primaryRelative.SourceId}, Geni: {primaryRelative.GeniId})");

        if (additionalRelatives != null && additionalRelatives.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Additional:");
            foreach (var relative in additionalRelatives)
            {
                Console.WriteLine($"    • {relative.RelationType} of {relative.Name}");
                Console.WriteLine($"      (Source: {relative.SourceId}, Geni: {relative.GeniId})");
            }
        }

        Console.WriteLine("───────────────────────────────────────────────────────────────");

        return PromptUser();
    }

    /// <summary>
    /// Formats person name with maiden name if available
    /// </summary>
    private static string FormatPersonName(string? firstName, string? middleName, string? lastName, string? maidenName)
    {
        var name = string.Join(" ", new[] { firstName, middleName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        if (!string.IsNullOrEmpty(maidenName))
            name += $" (née {maidenName})";
        return name;
    }

    /// <summary>
    /// Formats life event (birth/death) with date and place
    /// </summary>
    private static string FormatLifeEvent(string? date, string? place)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(date))
            parts.Add(date);
        else
            parts.Add("Unknown");

        if (!string.IsNullOrEmpty(place))
            parts.Add($"in {place}");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Confirms profile update with user
    /// </summary>
    public ConfirmationResult ConfirmUpdateProfile(
        string sourceId,
        string geniId,
        List<FieldDiff> fieldsToUpdate)
    {
        if (!_isEnabled)
            return ConfirmationResult.Approved;

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("UPDATE PROFILE - CONFIRMATION REQUIRED");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"Source ID:        {sourceId}");
        Console.WriteLine($"Geni ID:          {geniId}");
        Console.WriteLine();
        Console.WriteLine("Fields to update:");

        foreach (var field in fieldsToUpdate)
        {
            Console.WriteLine($"  • {field.FieldName}");
            Console.WriteLine($"    Current:  {field.DestinationValue ?? "(empty)"}");
            Console.WriteLine($"    New:      {field.SourceValue ?? "(empty)"}");
            Console.WriteLine($"    Action:   {field.Action}");

            if (field.PhotoSimilarity.HasValue)
                Console.WriteLine($"    Similarity: {field.PhotoSimilarity.Value:P0}");

            Console.WriteLine();
        }
        Console.WriteLine("───────────────────────────────────────────────────────────────");

        return PromptUser();
    }

    /// <summary>
    /// Prompts user for decision
    /// </summary>
    private ConfirmationResult PromptUser()
    {
        while (true)
        {
            Console.Write("Approve [Y]es / [N]o (skip) / [A]bort? ");
            var input = Console.ReadLine()?.Trim().ToUpperInvariant();

            switch (input)
            {
                case "Y":
                case "YES":
                    Console.WriteLine("✓ Approved");
                    return ConfirmationResult.Approved;

                case "N":
                case "NO":
                case "S":
                case "SKIP":
                    Console.WriteLine("⊘ Skipped");
                    return ConfirmationResult.Skipped;

                case "A":
                case "ABORT":
                case "Q":
                case "QUIT":
                    Console.WriteLine("✗ Aborted by user");
                    return ConfirmationResult.Aborted;

                default:
                    Console.WriteLine("Invalid input. Please enter Y (yes), N (no/skip), or A (abort).");
                    continue;
            }
        }
    }
}

/// <summary>
/// Result of user confirmation
/// </summary>
public enum ConfirmationResult
{
    /// <summary>
    /// User approved the operation
    /// </summary>
    Approved,

    /// <summary>
    /// User chose to skip this operation
    /// </summary>
    Skipped,

    /// <summary>
    /// User chose to abort the entire process
    /// </summary>
    Aborted
}

/// <summary>
/// Information about a relative for display in confirmation prompts
/// </summary>
public class RelativeInfo
{
    public required string SourceId { get; init; }
    public required string GeniId { get; init; }
    public required string Name { get; init; }
    public string? RelationType { get; init; }
}
