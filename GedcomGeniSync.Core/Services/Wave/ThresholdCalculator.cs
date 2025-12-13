using GedcomGeniSync.Core.Models.Wave;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Вычисление адаптивных порогов соответствия в зависимости от контекста.
/// В контексте семьи пороги могут быть ниже, чем при глобальном поиске.
/// </summary>
public class ThresholdCalculator
{
    private readonly ThresholdStrategy _strategy;
    private readonly int _baseThreshold;

    public ThresholdCalculator(ThresholdStrategy strategy, int baseThreshold = 60)
    {
        _strategy = strategy;
        _baseThreshold = baseThreshold;
    }

    /// <summary>
    /// Получить порог для сопоставления в зависимости от типа связи и количества кандидатов.
    /// </summary>
    /// <param name="relation">Тип родственной связи</param>
    /// <param name="candidateCount">Количество кандидатов для сопоставления</param>
    /// <returns>Порог соответствия (0-100)</returns>
    public int GetThreshold(RelationType relation, int candidateCount)
    {
        if (_strategy == ThresholdStrategy.Fixed)
            return _baseThreshold;

        // Базовые пороги для Adaptive стратегии
        var baseByRelation = relation switch
        {
            RelationType.Anchor => 100,   // Якорь всегда 100%
            RelationType.Spouse => 40,    // Супруги в семье
            RelationType.Parent => 45,    // Родители
            RelationType.Child => 50,     // Дети
            RelationType.Sibling => 55,   // Сиблинги
            _ => 60
        };

        // Корректировка по количеству кандидатов
        var adjustment = candidateCount switch
        {
            1 => -5,        // Один кандидат — можно снизить порог
            2 => 0,         // Два кандидата — базовый порог
            3 or 4 => 5,    // Несколько кандидатов — чуть повысить
            >= 5 and <= 8 => 10,   // Много кандидатов — повысить заметно
            _ => 15         // Очень много кандидатов — максимальное повышение
        };

        var threshold = baseByRelation + adjustment;

        // Корректировка по стратегии
        threshold += _strategy switch
        {
            ThresholdStrategy.Aggressive => -10,      // Агрессивная — ниже пороги
            ThresholdStrategy.Conservative => 15,     // Консервативная — выше пороги
            _ => 0
        };

        // Ограничиваем диапазон 30-85
        return Math.Clamp(threshold, 30, 85);
    }

    /// <summary>
    /// Получить минимальный порог для детей в семье с учётом размера семьи.
    /// </summary>
    public int GetChildThreshold(int childrenCount)
    {
        return GetThreshold(RelationType.Child, childrenCount);
    }

    /// <summary>
    /// Получить порог для супруга (обычно один кандидат).
    /// </summary>
    public int GetSpouseThreshold()
    {
        return GetThreshold(RelationType.Spouse, candidateCount: 1);
    }

    /// <summary>
    /// Получить порог для родителя (обычно 1-2 кандидата).
    /// </summary>
    public int GetParentThreshold(int parentCount = 2)
    {
        return GetThreshold(RelationType.Parent, parentCount);
    }
}
