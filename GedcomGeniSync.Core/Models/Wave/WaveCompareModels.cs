namespace GedcomGeniSync.Core.Models.Wave;

/// <summary>
/// Результат сопоставления одной персоны между деревьями.
/// Содержит метаданные о том, как и когда сопоставление было найдено.
/// </summary>
public record PersonMapping
{
    /// <summary>ID персоны в source дереве</summary>
    public required string SourceId { get; init; }

    /// <summary>ID персоны в destination дереве</summary>
    public required string DestinationId { get; init; }

    /// <summary>Оценка соответствия (0-100)</summary>
    public required int MatchScore { get; init; }

    /// <summary>На каком уровне от якоря найдено (0 = якорь)</summary>
    public required int Level { get; init; }

    /// <summary>Через какой тип связи найдено</summary>
    public required RelationType FoundVia { get; init; }

    /// <summary>ID семьи, через которую найдено (если применимо)</summary>
    public string? FoundInFamilyId { get; init; }

    /// <summary>ID персоны, от которой распространилось сопоставление</summary>
    public string? FoundFromPersonId { get; init; }

    /// <summary>Время нахождения сопоставления</summary>
    public DateTime FoundAt { get; init; }
}

/// <summary>Тип связи, через которую найдено сопоставление</summary>
public enum RelationType
{
    Anchor,     // Якорь — указан пользователем
    Spouse,     // Супруг
    Parent,     // Родитель
    Child,      // Ребёнок
    Sibling     // Брат/сестра
}

/// <summary>
/// Полный результат сравнения двух деревьев по волновому алгоритму.
/// </summary>
public record WaveCompareResult
{
    /// <summary>Путь к source файлу</summary>
    public required string SourceFile { get; init; }

    /// <summary>Путь к destination файлу</summary>
    public required string DestinationFile { get; init; }

    /// <summary>Время выполнения сравнения</summary>
    public required DateTime ComparedAt { get; init; }

    /// <summary>Информация о якорях</summary>
    public required AnchorInfo Anchors { get; init; }

    /// <summary>Параметры сравнения</summary>
    public required WaveCompareOptions Options { get; init; }

    /// <summary>Все найденные сопоставления</summary>
    public required IReadOnlyList<PersonMapping> Mappings { get; init; }

    /// <summary>Персоны из source, для которых не найдено соответствие</summary>
    public required IReadOnlyList<UnmatchedPerson> UnmatchedSource { get; init; }

    /// <summary>Персоны из destination, которые не были сопоставлены</summary>
    public required IReadOnlyList<UnmatchedPerson> UnmatchedDestination { get; init; }

    /// <summary>Проблемы валидации</summary>
    public required IReadOnlyList<ValidationIssue> ValidationIssues { get; init; }

    /// <summary>Статистика по уровням</summary>
    public required IReadOnlyList<LevelStatistics> StatisticsByLevel { get; init; }

    /// <summary>Общая статистика</summary>
    public required CompareStatistics Statistics { get; init; }
}

public record AnchorInfo
{
    public required string SourceId { get; init; }
    public required string DestinationId { get; init; }
    public string? SourcePersonSummary { get; init; }
    public string? DestinationPersonSummary { get; init; }
}

public record WaveCompareOptions
{
    /// <summary>Максимальный уровень распространения от якоря</summary>
    public int MaxLevel { get; init; } = 3;

    /// <summary>Стратегия выбора порогов</summary>
    public ThresholdStrategy ThresholdStrategy { get; init; } = ThresholdStrategy.Adaptive;

    /// <summary>Базовый порог для fuzzy match (используется при Fixed стратегии)</summary>
    public int BaseThreshold { get; init; } = 60;

    /// <summary>Путь к файлу с подтверждёнными пользователем соответствиями</summary>
    public string? ConfirmedMappingsFile { get; init; }

    /// <summary>Интерактивный режим — запрашивать пользователя при низком confidence</summary>
    public bool Interactive { get; init; } = false;

    /// <summary>Порог для автоматического принятия соответствий (без запроса пользователю)</summary>
    public int LowConfidenceThreshold { get; init; } = 70;

    /// <summary>Минимальный порог для запроса пользователю (ниже этого — автоотклонение)</summary>
    public int MinConfidenceThreshold { get; init; } = 50;

    /// <summary>Максимальное количество кандидатов для показа пользователю</summary>
    public int MaxCandidates { get; init; } = 5;
}

public enum ThresholdStrategy
{
    /// <summary>Фиксированный порог для всех случаев</summary>
    Fixed,

    /// <summary>Адаптивный порог в зависимости от контекста</summary>
    Adaptive,

    /// <summary>Агрессивный — низкие пороги, больше сопоставлений</summary>
    Aggressive,

    /// <summary>Консервативный — высокие пороги, меньше ошибок</summary>
    Conservative
}

public record LevelStatistics
{
    public int Level { get; init; }
    public int PersonsProcessed { get; init; }
    public int NewMappingsFound { get; init; }
    public int FamiliesProcessed { get; init; }
    public TimeSpan Duration { get; init; }
}

public record UnmatchedPerson
{
    public required string Id { get; init; }
    public required string PersonSummary { get; init; }
    public int? NearestMatchedLevel { get; init; }
    public string? NearestMatchedPersonId { get; init; }
}

public record CompareStatistics
{
    public int TotalSourcePersons { get; init; }
    public int TotalDestinationPersons { get; init; }
    public int TotalMappings { get; init; }
    public int UnmatchedSourceCount { get; init; }
    public int UnmatchedDestinationCount { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int ValidationIssuesCount { get; init; }
}

public record ValidationIssue
{
    public required Severity Severity { get; init; }
    public required IssueType Type { get; init; }
    public string? SourceId { get; init; }
    public string? DestId { get; init; }
    public required string Message { get; init; }
}

public enum Severity { Low, Medium, High }

public enum IssueType
{
    GenderMismatch,
    BirthYearMismatch,
    DeathYearMismatch,
    DuplicateMapping,
    FamilyInconsistency,
    LowMatchScore,
    InvalidSourceId,
    InvalidDestId
}

/// <summary>
/// Контекст для сопоставления членов одной семьи.
/// Содержит информацию о семье и уже известных сопоставлениях.
/// </summary>
public class FamilyMatchContext
{
    /// <summary>Семья в source дереве</summary>
    public required FamilyRecord SourceFamily { get; init; }

    /// <summary>Семья в destination дереве (если найдена)</summary>
    public FamilyRecord? DestinationFamily { get; set; }

    /// <summary>Текущие известные сопоставления</summary>
    public required IReadOnlyDictionary<string, string> ExistingMappings { get; init; }

    /// <summary>Текущий уровень</summary>
    public required int CurrentLevel { get; init; }

    /// <summary>ID персоны, от которой мы пришли к этой семье</summary>
    public required string FromPersonId { get; init; }
}

/// <summary>Роль персоны в семье</summary>
public enum FamilyRole
{
    Spouse,  // Персона — супруг/родитель в этой семье
    Child    // Персона — ребёнок в этой семье
}

/// <summary>
/// High-confidence report generated from wave compare results.
/// Contains lists of individuals to add and update based on confidence threshold.
/// </summary>
public record WaveHighConfidenceReport
{
    /// <summary>Source GEDCOM file path</summary>
    public required string SourceFile { get; init; }

    /// <summary>Destination GEDCOM file path</summary>
    public required string DestinationFile { get; init; }

    /// <summary>Anchor information</summary>
    public required AnchorInfo Anchors { get; init; }

    /// <summary>Wave compare options used</summary>
    public required WaveCompareOptions Options { get; init; }

    /// <summary>Individual comparison results with high-confidence matches</summary>
    public required WaveIndividualsReport Individuals { get; init; }
}

/// <summary>
/// Individual results from wave compare with high-confidence filtering.
/// Contains lists of nodes to update and add, similar to regular compare command.
/// </summary>
public record WaveIndividualsReport
{
    /// <summary>Nodes that exist in both trees but need updates</summary>
    public required System.Collections.Immutable.ImmutableList<GedcomGeniSync.Models.NodeToUpdate> NodesToUpdate { get; init; }

    /// <summary>Nodes to add to destination tree</summary>
    public required System.Collections.Immutable.ImmutableList<GedcomGeniSync.Models.NodeToAdd> NodesToAdd { get; init; }
}
