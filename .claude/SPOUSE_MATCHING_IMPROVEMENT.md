# Улучшение сопоставления супругов

## Текущая проблема

### Архитектура
```
WaveCompareService
  └─> FamilyMatcher.FindMatchingFamily()
       └─> Returns: ОДНА лучшая семья
            └─> FamilyMemberMatcher.MatchMembers(destFamily)
                 └─> MatchSpouse(sourceHusbandId, destFamily.HusbandId)
                      └─> Compare ОДИН кандидат
```

**Проблема**: Супруг берется из уже выбранной семьи без рассмотрения альтернатив.

### Пример проблемного сценария

```
Source Family @F1@:
  Husband: Александр Махин (@I500002@) [already mapped]
  Wife: Магдалина Зайцева (@I500005@) [to be matched]

Destination Families:

  Family @F100@:
    Husband: Александр Махин (@I6000...827@) [matched!]
    Wife: Дарья Клименко (@I6000...738@) [score with Магдалина: 41%]
    Score: 60 (Husband Match: 50 + Wife Present: 10)

  Family @F101@:
    Husband: Александр Махин (@I6000...827@) [matched!]
    Wife: Магдалина Зайцева (@I6000...999@) [score with Магдалина: 95%]
    Score: 60 (Husband Match: 50 + Wife Present: 10)
```

**Текущее поведение:**
1. FamilyMatcher выбирает первую семью с лучшим структурным score
2. Обе семьи имеют score = 60 (одинаковый!)
3. Выбирается первая: @F100@ с Дарьей (41%)
4. Магдалина сопоставляется с Дарьей (НЕПРАВИЛЬНО!)

**Ожидаемое поведение:**
1. Найти все подходящие семьи (score > 0)
2. Собрать всех кандидатов на роль жены из этих семей
3. Сравнить Магдалину со всеми кандидатами:
   - Дарья: 41%
   - Магдалина: 95%
4. Выбрать лучшую: Магдалина (95%)
5. Сопоставить с семьей @F101@

## Решение 1: Множественные кандидаты в FamilyMemberMatcher

### Идея
Передавать в `MatchMembers` не одну семью, а **все подходящие семьи**.

### Изменения

#### 1. FamilyMatcher возвращает ВСЕ подходящие семьи

```csharp
public List<FamilyRecord> FindMatchingFamilies(
    FamilyRecord sourceFamily,
    IEnumerable<FamilyRecord> destFamilies,
    IReadOnlyDictionary<string, PersonMapping> mappings,
    int minScore = 10)  // Минимальный структурный score
{
    var matches = new List<(FamilyRecord family, int score)>();

    foreach (var destFamily in destFamilies)
    {
        var (score, hasConflict) = CalculateFamilyMatchScore(...);

        if (!hasConflict && score >= minScore)
        {
            matches.Add((destFamily, score));
        }
    }

    return matches
        .OrderByDescending(m => m.score)
        .Select(m => m.family)
        .ToList();
}
```

#### 2. FamilyMemberMatcher собирает кандидатов из всех семей

```csharp
public List<PersonMapping> MatchMembers(
    FamilyMatchContext context,
    List<FamilyRecord> candidateFamilies,  // ← СПИСОК семей
    TreeGraph sourceTree,
    TreeGraph destTree)
{
    var sourceFamily = context.SourceFamily;

    // Собираем всех кандидатов на роль мужа
    var husbandCandidates = candidateFamilies
        .Where(f => f.HusbandId != null)
        .Select(f => (familyId: f.Id, personId: f.HusbandId))
        .ToList();

    // Выбираем лучшего мужа
    if (sourceFamily.HusbandId != null && !existingMappings.ContainsKey(sourceFamily.HusbandId))
    {
        var bestHusband = FindBestMatch(
            sourceFamily.HusbandId,
            husbandCandidates.Select(c => c.personId),
            sourceTree,
            destTree,
            RelationType.Spouse);

        if (bestHusband != null)
        {
            newMappings.Add(bestHusband);
            // Выбираем семью с этим мужем для дальнейшей обработки
            selectedFamily = candidateFamilies.First(f => f.HusbandId == bestHusband.DestinationId);
        }
    }

    // Аналогично для жены и детей
}

private PersonMapping? FindBestMatch(
    string sourceId,
    IEnumerable<string> candidateIds,
    TreeGraph sourceTree,
    TreeGraph destTree,
    RelationType relationType)
{
    PersonMapping? bestMapping = null;
    int bestScore = 0;

    var threshold = _thresholdCalculator.GetThreshold(relationType, candidateIds.Count());

    foreach (var candId in candidateIds)
    {
        var sourcePerson = sourceTree.PersonsById[sourceId];
        var destPerson = destTree.PersonsById[candId];

        var result = _fuzzyMatcher.Compare(sourcePerson, destPerson);

        if (result.Score > bestScore && result.Score >= threshold)
        {
            bestScore = (int)result.Score;
            bestMapping = new PersonMapping { ... };
        }
    }

    return bestMapping;
}
```

## Решение 2: Улучшенный FamilyMatcher

### Идея
При выборе лучшей семьи учитывать не только структурный score, но и **персональные score** супругов.

### Комбинированный score

```csharp
private (int totalScore, FamilyRecord? family) FindBestFamilyWithPersonScores(
    FamilyRecord sourceFamily,
    IEnumerable<FamilyRecord> destFamilies,
    IReadOnlyDictionary<string, PersonMapping> mappings,
    TreeGraph sourceTree,
    TreeGraph destTree)
{
    FamilyRecord? bestFamily = null;
    int bestScore = 0;

    foreach (var destFamily in destFamilies)
    {
        // 1. Структурный score (0-100)
        var (structureScore, hasConflict) = CalculateFamilyMatchScore(...);
        if (hasConflict) continue;

        // 2. Персональный score для супругов (0-100 каждый)
        int husbandScore = 0;
        int wifeScore = 0;

        if (sourceFamily.HusbandId != null && destFamily.HusbandId != null &&
            !mappings.ContainsKey(sourceFamily.HusbandId))
        {
            var result = _fuzzyMatcher.Compare(
                sourceTree.PersonsById[sourceFamily.HusbandId],
                destTree.PersonsById[destFamily.HusbandId]);
            husbandScore = (int)result.Score;
        }

        if (sourceFamily.WifeId != null && destFamily.WifeId != null &&
            !mappings.ContainsKey(sourceFamily.WifeId))
        {
            var result = _fuzzyMatcher.Compare(
                sourceTree.PersonsById[sourceFamily.WifeId],
                destTree.PersonsById[destFamily.WifeId]);
            wifeScore = (int)result.Score;
        }

        // 3. Комбинированный score
        // Вес: 40% структура, 30% муж, 30% жена
        int totalScore = (int)(
            structureScore * 0.4 +
            husbandScore * 0.3 +
            wifeScore * 0.3);

        if (totalScore > bestScore)
        {
            bestScore = totalScore;
            bestFamily = destFamily;
        }
    }

    return (bestScore, bestFamily);
}
```

## Сравнение решений

| Аспект | Решение 1 (Множественные кандидаты) | Решение 2 (Комбинированный score) |
|--------|--------------------------------------|-------------------------------------|
| **Сложность** | Высокая (нужно переписать много кода) | Средняя (локальные изменения в FamilyMatcher) |
| **Точность** | Очень высокая (рассматриваются ВСЕ варианты) | Высокая (лучшая семья = лучшие супруги) |
| **Производительность** | Средняя (больше сравнений) | Хорошая (оптимизировано) |
| **Maintainability** | Средняя (сложная логика) | Хорошая (понятная логика) |

## Рекомендация

**Начать с Решения 2** (комбинированный score) по причинам:

1. ✅ **Проще реализовать** - локальные изменения только в FamilyMatcher
2. ✅ **Решает основную проблему** - учитывает персональное сходство при выборе семьи
3. ✅ **Не ломает существующую архитектуру**
4. ✅ **Легко тестировать**

Затем, если нужно, можно добавить **Решение 1** для еще большей точности.

## Пример с комбинированным score

```
Source Family @F1@:
  Husband: Александр [mapped]
  Wife: Магдалина

Destination Families:

  Family @F100@:
    Husband: Александр [mapped]
    Wife: Дарья
    Structure Score: 60
    Wife Personal Score: 41
    Total: 60*0.4 + 41*0.3 = 24 + 12.3 = 36.3

  Family @F101@:
    Husband: Александр [mapped]
    Wife: Магдалина
    Structure Score: 60
    Wife Personal Score: 95
    Total: 60*0.4 + 95*0.3 = 24 + 28.5 = 52.5 ← ЛУЧШЕ!
```

Семья @F101@ будет выбрана, и Магдалина будет правильно сопоставлена!

## Изменения в коде

### Файлы для изменения:

1. **FamilyMatcher.cs**
   - Добавить метод `FindBestFamilyWithPersonScores()`
   - Изменить `FindMatchingFamily()` чтобы использовать новый метод
   - Принимать `TreeGraph` параметры для доступа к персонам

2. **WaveCompareService.cs**
   - Передавать `sourceTree` и `destTree` в `FamilyMatcher.FindMatchingFamily()`

3. **FamilyMemberMatcher.cs**
   - Упростить `MatchSpouse()` - теперь супруг уже оптимально выбран FamilyMatcher

## Тестирование

Создать тест:
```csharp
[Fact]
public void FindMatchingFamily_ChoosesFamilyWithBestPersonalScores()
{
    // Arrange: 2 семьи с одинаковым структурным score
    // но разными персональными scores супругов

    // Act: FindMatchingFamily

    // Assert: Выбрана семья с лучшими персональными scores
}
```

---

**Статус**: 📋 Требует реализации
**Приоритет**: 🔴 Высокий
**Сложность**: ⭐⭐⭐ Средняя
**Время на реализацию**: 2-3 часа
