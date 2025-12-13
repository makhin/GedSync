using GedcomGeniSync.Core.Models.Wave;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Поиск соответствующей семьи в destination дереве на основе уже известных сопоставлений.
/// Использует структурный подход: если супруги сопоставлены, их семьи должны совпадать.
/// </summary>
public class FamilyMatcher
{
    private readonly ILogger<FamilyMatcher>? _logger;

    public FamilyMatcher(ILogger<FamilyMatcher>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Найти соответствующую семью в списке destination семей.
    /// Использует уже известные сопоставления персон для структурного поиска.
    /// </summary>
    /// <param name="sourceFamily">Семья из source дерева</param>
    /// <param name="destFamilies">Список кандидатов семей из destination дерева</param>
    /// <param name="mappings">Уже известные сопоставления персон (sourceId -> PersonMapping)</param>
    /// <returns>Лучшая подходящая семья или null если не найдено</returns>
    public FamilyRecord? FindMatchingFamily(
        FamilyRecord sourceFamily,
        IEnumerable<FamilyRecord> destFamilies,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        FamilyRecord? bestFamily = null;
        int bestScore = 0;

        foreach (var destFamily in destFamilies)
        {
            var (score, hasConflict) = CalculateFamilyMatchScore(
                sourceFamily,
                destFamily,
                mappings);

            if (!hasConflict && score > bestScore)
            {
                bestScore = score;
                bestFamily = destFamily;
            }
        }

        // Возвращаем только если есть хоть какое-то совпадение
        if (bestScore > 0)
        {
            _logger?.LogDebug(
                "Matched family {SourceId} -> {DestId} with score {Score}",
                sourceFamily.Id, bestFamily?.Id, bestScore);
            return bestFamily;
        }

        _logger?.LogDebug("No matching family found for {SourceId}", sourceFamily.Id);
        return null;
    }

    /// <summary>
    /// Вычислить score соответствия между двумя семьями.
    /// </summary>
    /// <returns>Tuple (score, hasConflict) где hasConflict указывает на несовместимость</returns>
    private (int score, bool hasConflict) CalculateFamilyMatchScore(
        FamilyRecord sourceFamily,
        FamilyRecord destFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        int score = 0;
        bool hasConflict = false;

        // ═══════════════════════════════════════════════════════════
        // Проверяем мужа
        // ═══════════════════════════════════════════════════════════
        if (sourceFamily.HusbandId != null)
        {
            if (mappings.TryGetValue(sourceFamily.HusbandId, out var husbandMapping))
            {
                // Муж уже сопоставлен - проверяем совпадение
                if (husbandMapping.DestinationId == destFamily.HusbandId)
                {
                    score += 50;  // Муж совпадает — большой плюс
                }
                else if (destFamily.HusbandId != null)
                {
                    hasConflict = true;  // Муж сопоставлен с другим — конфликт
                }
            }
            else if (destFamily.HusbandId != null)
            {
                score += 10;  // Оба имеют мужа, но ещё не сопоставлен
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Проверяем жену
        // ═══════════════════════════════════════════════════════════
        if (sourceFamily.WifeId != null)
        {
            if (mappings.TryGetValue(sourceFamily.WifeId, out var wifeMapping))
            {
                // Жена уже сопоставлена - проверяем совпадение
                if (wifeMapping.DestinationId == destFamily.WifeId)
                {
                    score += 50;  // Жена совпадает — большой плюс
                }
                else if (destFamily.WifeId != null)
                {
                    hasConflict = true;  // Жена сопоставлена с другой — конфликт
                }
            }
            else if (destFamily.WifeId != null)
            {
                score += 10;  // Обе имеют жену, но ещё не сопоставлена
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Проверяем детей
        // ═══════════════════════════════════════════════════════════
        int matchedChildrenCount = 0;
        foreach (var childId in sourceFamily.ChildIds)
        {
            if (mappings.TryGetValue(childId, out var childMapping))
            {
                if (destFamily.ChildIds.Contains(childMapping.DestinationId))
                {
                    matchedChildrenCount++;
                }
                else
                {
                    // Ребёнок сопоставлен с кем-то из другой семьи — конфликт
                    hasConflict = true;
                }
            }
        }

        score += matchedChildrenCount * 20;  // Каждый совпавший ребёнок добавляет 20 очков

        return (score, hasConflict);
    }

    /// <summary>
    /// Проверить, является ли семья уже полностью сопоставленной.
    /// </summary>
    public bool IsFamilyFullyMapped(
        FamilyRecord sourceFamily,
        IReadOnlyDictionary<string, PersonMapping> mappings)
    {
        // Проверяем супругов
        if (sourceFamily.HusbandId != null && !mappings.ContainsKey(sourceFamily.HusbandId))
            return false;

        if (sourceFamily.WifeId != null && !mappings.ContainsKey(sourceFamily.WifeId))
            return false;

        // Проверяем всех детей
        foreach (var childId in sourceFamily.ChildIds)
        {
            if (!mappings.ContainsKey(childId))
                return false;
        }

        return true;
    }
}
