using GedcomGeniSync.Models;

namespace GedcomGeniSync.Core.Models.Wave;

/// <summary>
/// Представление генеалогического дерева в виде графа с индексами для быстрого доступа.
/// Создаётся один раз при загрузке и используется для навигации.
/// </summary>
public class TreeGraph
{
    /// <summary>Все персоны, индексированные по ID</summary>
    public required IReadOnlyDictionary<string, PersonRecord> PersonsById { get; init; }

    /// <summary>Все семьи, индексированные по ID</summary>
    public required IReadOnlyDictionary<string, FamilyRecord> FamiliesById { get; init; }

    /// <summary>
    /// Обратный индекс: персона → список семей, где она супруг/родитель.
    /// Позволяет быстро найти семьи, которые человек создал.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> PersonToFamiliesAsSpouse { get; init; }

    /// <summary>
    /// Обратный индекс: персона → список семей, где она ребёнок.
    /// Позволяет быстро найти родительскую семью.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> PersonToFamiliesAsChild { get; init; }

    /// <summary>
    /// Опциональный индекс: год рождения → список персон.
    /// Используется для предварительной фильтрации при fuzzy match.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>>? PersonsByBirthYear { get; init; }

    /// <summary>
    /// Опциональный индекс: нормализованная фамилия → список персон.
    /// Используется для предварительной фильтрации при fuzzy match.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? PersonsByNormalizedLastName { get; init; }
}
