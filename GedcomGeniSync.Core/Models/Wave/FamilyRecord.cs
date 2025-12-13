using GedcomGeniSync.Models;

namespace GedcomGeniSync.Core.Models.Wave;

/// <summary>
/// Представление семьи (FAM record в GEDCOM).
/// Содержит ссылки на супругов и детей.
/// </summary>
public record FamilyRecord
{
    /// <summary>ID семьи (например, @F123@)</summary>
    public required string Id { get; init; }

    /// <summary>ID мужа/отца (HUSB)</summary>
    public string? HusbandId { get; init; }

    /// <summary>ID жены/матери (WIFE)</summary>
    public string? WifeId { get; init; }

    /// <summary>Список ID детей (CHIL)</summary>
    public required IReadOnlyList<string> ChildIds { get; init; }

    /// <summary>Дата брака</summary>
    public DateInfo? MarriageDate { get; init; }

    /// <summary>Место брака</summary>
    public string? MarriagePlace { get; init; }

    /// <summary>Дата развода</summary>
    public DateInfo? DivorceDate { get; init; }
}
