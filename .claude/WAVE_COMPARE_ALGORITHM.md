# Wave Compare Algorithm — Алгоритм волнового сравнения деревьев

## Оглавление

1. [Проблемы текущего подхода](#проблемы-текущего-подхода)
2. [Концепция нового алгоритма](#концепция-нового-алгоритма)
3. [Архитектура](#архитектура)
4. [Структуры данных](#структуры-данных)
5. [Алгоритмы](#алгоритмы)
6. [Стратегия порогов соответствия](#стратегия-порогов-соответствия)
7. [Индексы для ускорения](#индексы-для-ускорения)
8. [Валидация и коррекция ошибок](#валидация-и-коррекция-ошибок)
9. [Порядок имплементации](#порядок-имплементации)
10. [Структура файлов](#структура-файлов)
11. [Переиспользуемые компоненты](#переиспользуемые-компоненты)

---

## Проблемы текущего подхода

### Текущая реализация (GedcomCompareService)

Текущий алгоритм в `Services/Compare/`:
- `GedcomCompareService.cs` — оркестратор с 5 итерациями
- `IndividualCompareService.cs` — сопоставление персон
- `FamilyCompareService.cs` — сопоставление семей
- `MappingValidationService.cs` — валидация

### Выявленные проблемы

1. **Глобальное сравнение**: Алгоритм пытается сопоставить всех людей сразу, а не расходится органично от якоря

2. **Итерации без структуры**: 5 итераций — это костыль, а не органичное распространение по дереву. Нет гарантии, что 5 итераций достаточно или что они оптимальны

3. **Отсутствие "семейного контекста"**: Fuzzy matching работает на уровне индивидов, не используя преимущества структуры семьи. В семье из 6 человек не нужен порог 70% — достаточно выбрать лучшего из 6 кандидатов

4. **Нечёткий контроль глубины**: Параметр `--depth` контролирует только добавление новых записей, а не само сравнение

5. **Сложность отладки**: Итеративный подход затрудняет понимание, почему конкретное сопоставление было или не было найдено

---

## Концепция нового алгоритма

### Wave Propagation Algorithm (Алгоритм волнового распространения)

**Ключевая метафора**: Капля в воду — волны расходятся кругами. Сопоставление распространяется от якорной персоны по связям, уровень за уровнем.

```
Уровень 0: Якорь (anchor) — известное соответствие, указанное пользователем
Уровень 1: Родители, супруги, дети, братья/сёстры якоря
Уровень 2: Родители супругов, дедушки/бабушки, внуки, племянники
Уровень 3: Прадедушки/прабабушки, правнуки, двоюродные братья/сёстры
...
```

### Почему это работает лучше

**1. В семье не нужен высокий порог соответствия:**
- У якоря 4 ребёнка в GEDCOM-1 и 4 ребёнка в GEDCOM-2
- Не нужно искать "лучшего кандидата из 10000 человек"
- Нужно найти соответствие между 4 и 4 — это комбинаторика, а не поиск иголки
- Достаточно сравнить имена и выбрать лучшие пары

**2. Структурная валидация:**
- Если сопоставили мужа и жену, их дети должны совпадать с детьми из той же семьи
- Это даёт дополнительную уверенность и ловит ошибки на ранней стадии

**3. Понятность и отлаживаемость:**
- Каждое сопоставление имеет чёткий путь от якоря
- Можно точно сказать: "Иван сопоставлен на уровне 2, через семью F123, как ребёнок"

**4. Контролируемая глубина:**
- Пользователь указывает `--max-level 3` и получает ровно 3 уровня родственников
- Никаких "может, найдём, может, нет"

---

## Архитектура

### Общая схема

```
┌─────────────────────────────────────────────────────────────────┐
│                     WaveCompareService                          │
│                    (главный оркестратор)                        │
├─────────────────────────────────────────────────────────────────┤
│  1. Загрузить деревья (GedcomLoader)                           │
│  2. Построить индексы (TreeIndexer)                            │
│  3. Инициализировать якорь                                      │
│  4. BFS-цикл по уровням:                                        │
│     ├─ Взять персону из очереди                                │
│     ├─ Найти все её семьи                                       │
│     ├─ Для каждой семьи → FamilyMatcher                        │
│     ├─ Сопоставить членов → FamilyMemberMatcher                │
│     ├─ Валидировать → WaveMappingValidator                     │
│     └─ Добавить новые сопоставления в очередь                  │
│  5. Сформировать результат                                      │
└─────────────────────────────────────────────────────────────────┘
         │                    │                    │
         ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│   TreeIndexer   │  │  FamilyMatcher  │  │ FamilyMember    │
│                 │  │                 │  │ Matcher         │
│ - Построение    │  │ - Поиск соотв.  │  │                 │
│   индексов      │  │   семьи в dest  │  │ - Сопоставление │
│ - Обратные      │  │ - Оценка по     │  │   супругов      │
│   индексы       │  │   сопоставл.    │  │ - Сопоставление │
│                 │  │   членам        │  │   детей         │
└─────────────────┘  └─────────────────┘  └─────────────────┘
         │                    │                    │
         └────────────────────┼────────────────────┘
                              ▼
                    ┌─────────────────┐
                    │ TreeNavigator   │
                    │                 │
                    │ - Навигация     │
                    │   по графу      │
                    │ - GetFamilies   │
                    │ - GetRelatives  │
                    └─────────────────┘
```

### Поток данных

```
Source GEDCOM ──┐                              ┌── WaveCompareResult
                │    ┌─────────────────┐       │   - Mappings[]
                ├───►│ WaveCompare     │───────┤   - Unmatched[]
                │    │ Service         │       │   - Statistics
Dest GEDCOM ────┘    └─────────────────┘       └── Validation Issues
                              ▲
                              │
                     Anchor IDs (source, dest)
                     Max Level
                     Threshold Strategy
```

---

## Структуры данных

### TreeGraph — Граф дерева с индексами

```csharp
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
```

### FamilyRecord — Запись о семье

```csharp
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
```

### PersonMapping — Результат сопоставления персоны

```csharp
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
```

### WaveCompareResult — Итоговый результат сравнения

```csharp
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

public record WaveCompareOptions
{
    /// <summary>Максимальный уровень распространения от якоря</summary>
    public int MaxLevel { get; init; } = 3;

    /// <summary>Стратегия выбора порогов</summary>
    public ThresholdStrategy ThresholdStrategy { get; init; } = ThresholdStrategy.Adaptive;

    /// <summary>Базовый порог для fuzzy match (используется при Fixed стратегии)</summary>
    public int BaseThreshold { get; init; } = 60;
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
```

### FamilyMatchContext — Контекст сопоставления семьи

```csharp
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
```

---

## Алгоритмы

### Основной алгоритм — WaveCompare

```
ALGORITHM WaveCompare(sourceTree, destTree, anchorSource, anchorDest, maxLevel):

    // ═══════════════════════════════════════════════════════════
    // ИНИЦИАЛИЗАЦИЯ
    // ═══════════════════════════════════════════════════════════

    mappings = new Dictionary<string, PersonMapping>()

    // Добавляем якорь как первое сопоставление
    mappings[anchorSource] = new PersonMapping(
        SourceId: anchorSource,
        DestinationId: anchorDest,
        MatchScore: 100,
        Level: 0,
        FoundVia: RelationType.Anchor
    )

    // BFS очередь: (sourcePersonId, level)
    queue = new Queue<(string, int)>()
    queue.Enqueue((anchorSource, 0))

    // Множество обработанных персон (чтобы не обрабатывать дважды)
    processed = new HashSet<string> { anchorSource }

    // ═══════════════════════════════════════════════════════════
    // ОСНОВНОЙ ЦИКЛ BFS
    // ═══════════════════════════════════════════════════════════

    WHILE queue is not empty:
        (currentSourceId, level) = queue.Dequeue()

        // Проверяем ограничение глубины
        IF level >= maxLevel:
            CONTINUE

        // Получаем сопоставленный ID в destination
        currentDestId = mappings[currentSourceId].DestinationId

        // ─────────────────────────────────────────────────────────
        // Обрабатываем семьи, где персона — СУПРУГ/РОДИТЕЛЬ
        // ─────────────────────────────────────────────────────────

        sourceFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(sourceTree, currentSourceId)
        destFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(destTree, currentDestId)

        FOR EACH sourceFamily IN sourceFamiliesAsSpouse:
            // Находим соответствующую семью в destination
            destFamily = FamilyMatcher.FindMatchingFamily(
                sourceFamily,
                destFamiliesAsSpouse,
                mappings
            )

            IF destFamily is not null:
                // Сопоставляем членов семьи
                context = new FamilyMatchContext(
                    SourceFamily: sourceFamily,
                    DestinationFamily: destFamily,
                    ExistingMappings: mappings,
                    CurrentLevel: level,
                    FromPersonId: currentSourceId
                )

                newMappings = FamilyMemberMatcher.MatchMembers(context, sourceTree, destTree)

                FOR EACH mapping IN newMappings:
                    IF mapping.SourceId NOT IN processed:
                        // Валидируем новое сопоставление
                        IF WaveMappingValidator.Validate(mapping, mappings, sourceTree, destTree):
                            mappings[mapping.SourceId] = mapping
                            queue.Enqueue((mapping.SourceId, level + 1))
                            processed.Add(mapping.SourceId)

        // ─────────────────────────────────────────────────────────
        // Обрабатываем семьи, где персона — РЕБЁНОК
        // ─────────────────────────────────────────────────────────

        sourceFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(sourceTree, currentSourceId)
        destFamiliesAsChild = TreeNavigator.GetFamiliesAsChild(destTree, currentDestId)

        FOR EACH sourceFamily IN sourceFamiliesAsChild:
            destFamily = FamilyMatcher.FindMatchingFamily(
                sourceFamily,
                destFamiliesAsChild,
                mappings
            )

            IF destFamily is not null:
                context = new FamilyMatchContext(...)
                newMappings = FamilyMemberMatcher.MatchMembers(context, sourceTree, destTree)

                // Добавляем новые сопоставления (родители, сиблинги)
                FOR EACH mapping IN newMappings:
                    IF mapping.SourceId NOT IN processed:
                        IF WaveMappingValidator.Validate(mapping, ...):
                            mappings[mapping.SourceId] = mapping
                            queue.Enqueue((mapping.SourceId, level + 1))
                            processed.Add(mapping.SourceId)

    // ═══════════════════════════════════════════════════════════
    // ФОРМИРОВАНИЕ РЕЗУЛЬТАТА
    // ═══════════════════════════════════════════════════════════

    RETURN new WaveCompareResult(
        Mappings: mappings.Values,
        UnmatchedSource: FindUnmatched(sourceTree, mappings),
        UnmatchedDestination: FindUnmatchedDest(destTree, mappings),
        ...
    )
```

### Алгоритм поиска соответствующей семьи

```
ALGORITHM FindMatchingFamily(sourceFamily, destFamilies, mappings):

    bestFamily = null
    bestScore = 0

    FOR EACH destFamily IN destFamilies:
        score = 0
        hasConflict = false

        // ─────────────────────────────────────────────────────────
        // Проверяем мужа
        // ─────────────────────────────────────────────────────────
        IF sourceFamily.HusbandId is not null:
            IF sourceFamily.HusbandId IN mappings:
                mappedDestId = mappings[sourceFamily.HusbandId].DestinationId
                IF mappedDestId == destFamily.HusbandId:
                    score += 50  // Муж совпадает — большой плюс
                ELSE IF destFamily.HusbandId is not null:
                    hasConflict = true  // Муж сопоставлен с другим — конфликт
            ELSE IF destFamily.HusbandId is not null:
                score += 10  // Оба имеют мужа, но ещё не сопоставлен

        // ─────────────────────────────────────────────────────────
        // Проверяем жену
        // ─────────────────────────────────────────────────────────
        IF sourceFamily.WifeId is not null:
            IF sourceFamily.WifeId IN mappings:
                mappedDestId = mappings[sourceFamily.WifeId].DestinationId
                IF mappedDestId == destFamily.WifeId:
                    score += 50  // Жена совпадает — большой плюс
                ELSE IF destFamily.WifeId is not null:
                    hasConflict = true  // Жена сопоставлена с другой — конфликт
            ELSE IF destFamily.WifeId is not null:
                score += 10

        // ─────────────────────────────────────────────────────────
        // Проверяем детей
        // ─────────────────────────────────────────────────────────
        matchedChildrenCount = 0
        FOR EACH childId IN sourceFamily.ChildIds:
            IF childId IN mappings:
                IF mappings[childId].DestinationId IN destFamily.ChildIds:
                    matchedChildrenCount += 1
                ELSE:
                    hasConflict = true  // Ребёнок сопоставлен с кем-то из другой семьи

        score += matchedChildrenCount * 20

        // ─────────────────────────────────────────────────────────
        // Выбираем лучшую семью без конфликтов
        // ─────────────────────────────────────────────────────────
        IF NOT hasConflict AND score > bestScore:
            bestScore = score
            bestFamily = destFamily

    // Возвращаем только если есть хоть какое-то совпадение
    IF bestScore > 0:
        RETURN bestFamily
    ELSE:
        RETURN null
```

### Алгоритм сопоставления членов семьи

```
ALGORITHM MatchFamilyMembers(context, sourceTree, destTree):

    newMappings = []
    sourceFamily = context.SourceFamily
    destFamily = context.DestinationFamily
    existingMappings = context.ExistingMappings
    nextLevel = context.CurrentLevel + 1

    // ═══════════════════════════════════════════════════════════
    // 1. СОПОСТАВЛЕНИЕ СУПРУГОВ
    // ═══════════════════════════════════════════════════════════

    // Муж
    IF sourceFamily.HusbandId is not null
       AND sourceFamily.HusbandId NOT IN existingMappings
       AND destFamily.HusbandId is not null:

        sourcePerson = sourceTree.PersonsById[sourceFamily.HusbandId]
        destPerson = destTree.PersonsById[destFamily.HusbandId]

        score = FuzzyMatcher.Compare(sourcePerson, destPerson)
        threshold = GetThreshold(RelationType.Spouse, candidateCount: 1)

        IF score >= threshold:
            newMappings.Add(new PersonMapping(
                SourceId: sourceFamily.HusbandId,
                DestinationId: destFamily.HusbandId,
                MatchScore: score,
                Level: nextLevel,
                FoundVia: RelationType.Spouse,
                FoundInFamilyId: sourceFamily.Id,
                FoundFromPersonId: context.FromPersonId
            ))

    // Жена (аналогично)
    IF sourceFamily.WifeId is not null
       AND sourceFamily.WifeId NOT IN existingMappings
       AND destFamily.WifeId is not null:

        sourcePerson = sourceTree.PersonsById[sourceFamily.WifeId]
        destPerson = destTree.PersonsById[destFamily.WifeId]

        score = FuzzyMatcher.Compare(sourcePerson, destPerson)
        threshold = GetThreshold(RelationType.Spouse, candidateCount: 1)

        IF score >= threshold:
            newMappings.Add(new PersonMapping(...))

    // ═══════════════════════════════════════════════════════════
    // 2. СОПОСТАВЛЕНИЕ ДЕТЕЙ
    // ═══════════════════════════════════════════════════════════

    // Находим ещё не сопоставленных детей
    unmatchedSourceChildren = sourceFamily.ChildIds
        .Where(id => id NOT IN existingMappings)
        .Select(id => sourceTree.PersonsById[id])
        .ToList()

    unmatchedDestChildren = destFamily.ChildIds
        .Where(id => id NOT IN existingMappings.Values)
        .Select(id => destTree.PersonsById[id])
        .ToList()

    IF unmatchedSourceChildren.Count > 0 AND unmatchedDestChildren.Count > 0:
        childMappings = MatchChildrenSet(
            unmatchedSourceChildren,
            unmatchedDestChildren,
            nextLevel,
            sourceFamily.Id,
            context.FromPersonId
        )
        newMappings.AddRange(childMappings)

    RETURN newMappings
```

### Алгоритм сопоставления детей (жадный с матрицей)

```
ALGORITHM MatchChildrenSet(sourceChildren, destChildren, level, familyId, fromPersonId):

    n = sourceChildren.Count
    m = destChildren.Count

    // ═══════════════════════════════════════════════════════════
    // Строим матрицу схожести
    // ═══════════════════════════════════════════════════════════

    scores = new int[n, m]

    FOR i = 0 TO n-1:
        FOR j = 0 TO m-1:
            scores[i, j] = CompareChildInFamily(
                sourceChildren[i],
                destChildren[j],
                sourceIndex: i,
                destIndex: j
            )

    // ═══════════════════════════════════════════════════════════
    // Жадный алгоритм паросочетания
    // ═══════════════════════════════════════════════════════════

    // Собираем все пары с их оценками
    pairs = []
    FOR i = 0 TO n-1:
        FOR j = 0 TO m-1:
            pairs.Add((i, j, scores[i, j]))

    // Сортируем по убыванию оценки
    pairs.SortByDescending(p => p.score)

    // Жадно выбираем лучшие непересекающиеся пары
    usedSource = new HashSet<int>()
    usedDest = new HashSet<int>()
    mappings = []

    threshold = GetThreshold(RelationType.Child, candidateCount: Math.Min(n, m))

    FOR EACH (i, j, score) IN pairs:
        IF i NOT IN usedSource AND j NOT IN usedDest:
            IF score >= threshold:
                mappings.Add(new PersonMapping(
                    SourceId: sourceChildren[i].Id,
                    DestinationId: destChildren[j].Id,
                    MatchScore: score,
                    Level: level,
                    FoundVia: RelationType.Child,
                    FoundInFamilyId: familyId,
                    FoundFromPersonId: fromPersonId
                ))
                usedSource.Add(i)
                usedDest.Add(j)

    RETURN mappings


ALGORITHM CompareChildInFamily(source, dest, sourceIndex, destIndex):
    """
    Упрощённый scoring для детей одной семьи.
    В контексте семьи нам не нужен полный fuzzy match —
    достаточно сравнить имена и базовые характеристики.
    """

    score = 0

    // ─────────────────────────────────────────────────────────
    // Пол должен совпадать (обязательно)
    // ─────────────────────────────────────────────────────────
    IF source.Gender != dest.Gender
       AND source.Gender != Gender.Unknown
       AND dest.Gender != Gender.Unknown:
        RETURN 0  // Разный пол — точно не совпадают

    score += 15  // Бонус за совпадение пола

    // ─────────────────────────────────────────────────────────
    // Имя — основной критерий (до 60 очков)
    // ─────────────────────────────────────────────────────────
    nameScore = CompareNames(source, dest)  // Использует FuzzyMatcher
    score += (int)(nameScore * 0.6)

    // ─────────────────────────────────────────────────────────
    // Порядок рождения (если даты известны)
    // ─────────────────────────────────────────────────────────
    IF Math.Abs(sourceIndex - destIndex) <= 1:
        score += 10  // Похожий порядок среди детей
    ELSE IF Math.Abs(sourceIndex - destIndex) <= 2:
        score += 5

    // ─────────────────────────────────────────────────────────
    // Год рождения (мягкая проверка)
    // ─────────────────────────────────────────────────────────
    IF source.BirthYear.HasValue AND dest.BirthYear.HasValue:
        diff = Math.Abs(source.BirthYear.Value - dest.BirthYear.Value)
        score += diff SWITCH:
            0     => 15,
            1..2  => 10,
            3..5  => 5,
            _     => 0

    RETURN score  // Максимум ~100
```

---

## Стратегия порогов соответствия

### Концепция адаптивных порогов

В отличие от глобального fuzzy match, где нужен высокий порог (70-80%), в контексте семьи пороги могут быть ниже:

| Контекст | Количество кандидатов | Порог |
|----------|----------------------|-------|
| Супруг в семье | 1 | 40% |
| Супруг (несколько браков) | 2-3 | 55% |
| Родитель | 1-2 | 45% |
| Ребёнок (мало детей) | 1-4 | 50% |
| Ребёнок (много детей) | 5-8 | 60% |
| Ребёнок (очень много) | 9+ | 70% |
| Сиблинг | Зависит от размера | 50-65% |

### Реализация адаптивных порогов

```csharp
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

public class ThresholdCalculator
{
    private readonly ThresholdStrategy _strategy;
    private readonly int _baseThreshold;

    public int GetThreshold(RelationType relation, int candidateCount)
    {
        if (_strategy == ThresholdStrategy.Fixed)
            return _baseThreshold;

        // Базовые пороги для Adaptive стратегии
        var baseByRelation = relation switch
        {
            RelationType.Spouse => 40,
            RelationType.Parent => 45,
            RelationType.Child => 50,
            RelationType.Sibling => 55,
            _ => 60
        };

        // Корректировка по количеству кандидатов
        var adjustment = candidateCount switch
        {
            1 => -5,      // Один кандидат — можно снизить порог
            2 => 0,
            3..4 => 5,
            5..8 => 10,
            _ => 15       // Много кандидатов — повышаем порог
        };

        var threshold = baseByRelation + adjustment;

        // Корректировка по стратегии
        threshold += _strategy switch
        {
            ThresholdStrategy.Aggressive => -10,
            ThresholdStrategy.Conservative => 15,
            _ => 0
        };

        return Math.Clamp(threshold, 30, 85);
    }
}
```

### Когда использовать какую стратегию

| Стратегия | Когда использовать |
|-----------|-------------------|
| **Adaptive** (default) | Общий случай, хороший баланс |
| **Aggressive** | Деревья от одного источника, высокое качество данных |
| **Conservative** | Деревья из разных источников, много омонимов |
| **Fixed** | Для отладки, воспроизводимость результатов |

---

## Индексы для ускорения

### TreeIndexer — построение индексов

```csharp
public class TreeIndexer
{
    public TreeGraph BuildIndex(GedcomLoadResult loadResult)
    {
        var personToFamiliesAsSpouse = new Dictionary<string, List<string>>();
        var personToFamiliesAsChild = new Dictionary<string, List<string>>();

        // ═══════════════════════════════════════════════════════════
        // Строим обратные индексы по семьям
        // ═══════════════════════════════════════════════════════════

        foreach (var (famId, family) in loadResult.Families)
        {
            // Индекс: супруг → семьи
            if (family.HusbandId != null)
            {
                if (!personToFamiliesAsSpouse.ContainsKey(family.HusbandId))
                    personToFamiliesAsSpouse[family.HusbandId] = new List<string>();
                personToFamiliesAsSpouse[family.HusbandId].Add(famId);
            }

            if (family.WifeId != null)
            {
                if (!personToFamiliesAsSpouse.ContainsKey(family.WifeId))
                    personToFamiliesAsSpouse[family.WifeId] = new List<string>();
                personToFamiliesAsSpouse[family.WifeId].Add(famId);
            }

            // Индекс: ребёнок → семьи
            foreach (var childId in family.ChildIds)
            {
                if (!personToFamiliesAsChild.ContainsKey(childId))
                    personToFamiliesAsChild[childId] = new List<string>();
                personToFamiliesAsChild[childId].Add(famId);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Опциональные индексы для ускорения fuzzy match
        // ═══════════════════════════════════════════════════════════

        var personsByBirthYear = loadResult.Persons.Values
            .Where(p => p.BirthYear.HasValue)
            .GroupBy(p => p.BirthYear!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Id).ToList()
            );

        var personsByLastName = loadResult.Persons.Values
            .Where(p => !string.IsNullOrEmpty(p.NormalizedLastName))
            .GroupBy(p => p.NormalizedLastName!)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Id).ToList()
            );

        return new TreeGraph
        {
            PersonsById = loadResult.Persons,
            FamiliesById = loadResult.Families,
            PersonToFamiliesAsSpouse = personToFamiliesAsSpouse
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
            PersonToFamiliesAsChild = personToFamiliesAsChild
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
            PersonsByBirthYear = personsByBirthYear,
            PersonsByNormalizedLastName = personsByLastName
        };
    }
}
```

### TreeNavigator — методы навигации

```csharp
public static class TreeNavigator
{
    /// <summary>
    /// Получить все семьи, где персона является супругом/родителем.
    /// </summary>
    public static IEnumerable<FamilyRecord> GetFamiliesAsSpouse(TreeGraph tree, string personId)
    {
        if (tree.PersonToFamiliesAsSpouse.TryGetValue(personId, out var famIds))
            return famIds.Select(id => tree.FamiliesById[id]);
        return Enumerable.Empty<FamilyRecord>();
    }

    /// <summary>
    /// Получить все семьи, где персона является ребёнком.
    /// </summary>
    public static IEnumerable<FamilyRecord> GetFamiliesAsChild(TreeGraph tree, string personId)
    {
        if (tree.PersonToFamiliesAsChild.TryGetValue(personId, out var famIds))
            return famIds.Select(id => tree.FamiliesById[id]);
        return Enumerable.Empty<FamilyRecord>();
    }

    /// <summary>
    /// Получить всех ближайших родственников (родители, супруги, дети, сиблинги).
    /// </summary>
    public static IEnumerable<(string personId, RelationType relation)> GetImmediateRelatives(
        TreeGraph tree,
        string personId)
    {
        var relatives = new List<(string, RelationType)>();

        // Из семей как супруг: другой супруг + дети
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
        {
            // Супруг
            if (family.HusbandId != null && family.HusbandId != personId)
                relatives.Add((family.HusbandId, RelationType.Spouse));
            if (family.WifeId != null && family.WifeId != personId)
                relatives.Add((family.WifeId, RelationType.Spouse));

            // Дети
            foreach (var childId in family.ChildIds)
                relatives.Add((childId, RelationType.Child));
        }

        // Из семей как ребёнок: родители + сиблинги
        foreach (var family in GetFamiliesAsChild(tree, personId))
        {
            // Родители
            if (family.HusbandId != null)
                relatives.Add((family.HusbandId, RelationType.Parent));
            if (family.WifeId != null)
                relatives.Add((family.WifeId, RelationType.Parent));

            // Сиблинги
            foreach (var siblingId in family.ChildIds)
            {
                if (siblingId != personId)
                    relatives.Add((siblingId, RelationType.Sibling));
            }
        }

        return relatives.Distinct();
    }

    /// <summary>
    /// Получить все семьи персоны (как супруга и как ребёнка).
    /// </summary>
    public static IEnumerable<(FamilyRecord family, FamilyRole role)> GetAllFamilies(
        TreeGraph tree,
        string personId)
    {
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
            yield return (family, FamilyRole.Spouse);

        foreach (var family in GetFamiliesAsChild(tree, personId))
            yield return (family, FamilyRole.Child);
    }
}

public enum FamilyRole
{
    Spouse,  // Персона — супруг/родитель в этой семье
    Child    // Персона — ребёнок в этой семье
}
```

---

## Валидация и коррекция ошибок

### WaveMappingValidator

```csharp
public class WaveMappingValidator
{
    /// <summary>
    /// Валидирует одно сопоставление перед добавлением.
    /// Возвращает true, если сопоставление корректно.
    /// </summary>
    public ValidationResult ValidateMapping(
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree)
    {
        var issues = new List<ValidationIssue>();

        var sourcePerson = sourceTree.PersonsById[newMapping.SourceId];
        var destPerson = destTree.PersonsById[newMapping.DestinationId];

        // ═══════════════════════════════════════════════════════════
        // 1. Проверка пола
        // ═══════════════════════════════════════════════════════════
        if (sourcePerson.Gender != destPerson.Gender &&
            sourcePerson.Gender != Gender.Unknown &&
            destPerson.Gender != Gender.Unknown)
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.GenderMismatch,
                SourceId = newMapping.SourceId,
                DestId = newMapping.DestinationId,
                Message = $"Gender mismatch: {sourcePerson.Gender} vs {destPerson.Gender}"
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Проверка дат
        // ═══════════════════════════════════════════════════════════
        if (sourcePerson.BirthYear.HasValue && destPerson.BirthYear.HasValue)
        {
            var diff = Math.Abs(sourcePerson.BirthYear.Value - destPerson.BirthYear.Value);
            if (diff > 15)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.High,
                    Type = IssueType.BirthYearMismatch,
                    Message = $"Birth year differs by {diff} years"
                });
            }
            else if (diff > 5)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.BirthYearMismatch,
                    Message = $"Birth year differs by {diff} years"
                });
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Проверка на дубликаты
        // ═══════════════════════════════════════════════════════════

        // Проверяем, что destination ещё не сопоставлен с другим source
        var existingForDest = existingMappings.Values
            .FirstOrDefault(m => m.DestinationId == newMapping.DestinationId);

        if (existingForDest != null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = Severity.High,
                Type = IssueType.DuplicateMapping,
                Message = $"Destination {newMapping.DestinationId} already mapped to {existingForDest.SourceId}"
            });
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Проверка семейной консистентности
        // ═══════════════════════════════════════════════════════════
        ValidateFamilyConsistency(newMapping, existingMappings, sourceTree, destTree, issues);

        return new ValidationResult
        {
            IsValid = !issues.Any(i => i.Severity == Severity.High),
            Issues = issues
        };
    }

    private void ValidateFamilyConsistency(
        PersonMapping newMapping,
        IReadOnlyDictionary<string, PersonMapping> existingMappings,
        TreeGraph sourceTree,
        TreeGraph destTree,
        List<ValidationIssue> issues)
    {
        // Проверяем: если у новой персоны есть сопоставленные родители,
        // то она должна быть ребёнком в той же destination-семье

        var sourcePerson = sourceTree.PersonsById[newMapping.SourceId];
        var destPerson = destTree.PersonsById[newMapping.DestinationId];

        // Проверяем родителей
        if (sourcePerson.FatherId != null &&
            existingMappings.TryGetValue(sourcePerson.FatherId, out var fatherMapping))
        {
            // Отец сопоставлен — проверяем, что destPerson имеет того же отца
            if (destPerson.FatherId != fatherMapping.DestinationId)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = Severity.Medium,
                    Type = IssueType.FamilyInconsistency,
                    Message = $"Father mismatch: expected {fatherMapping.DestinationId}, got {destPerson.FatherId}"
                });
            }
        }

        // Аналогично для матери...
    }
}

public record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
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
    LowMatchScore
}
```

---

## Порядок имплементации

### Фаза 1: Подготовка инфраструктуры (1-2 дня)

| # | Задача | Файл | Зависимости |
|---|--------|------|-------------|
| 1.1 | Создать модели данных | `Models/Wave/TreeGraph.cs` | — |
| 1.2 | Создать PersonMapping и результаты | `Models/Wave/WaveCompareModels.cs` | — |
| 1.3 | Создать FamilyRecord (если нет) | `Models/Wave/FamilyRecord.cs` | — |
| 1.4 | Создать TreeIndexer | `Services/Wave/TreeIndexer.cs` | 1.1 |
| 1.5 | Создать TreeNavigator | `Services/Wave/TreeNavigator.cs` | 1.1 |
| 1.6 | Unit-тесты для индексов | `Tests/Wave/TreeIndexerTests.cs` | 1.4, 1.5 |

### Фаза 2: Ядро алгоритма (2-3 дня)

| # | Задача | Файл | Зависимости |
|---|--------|------|-------------|
| 2.1 | Создать ThresholdCalculator | `Services/Wave/ThresholdCalculator.cs` | — |
| 2.2 | Создать FamilyMatcher | `Services/Wave/FamilyMatcher.cs` | 1.1, 1.5 |
| 2.3 | Создать FamilyMemberMatcher | `Services/Wave/FamilyMemberMatcher.cs` | 2.1, FuzzyMatcherService |
| 2.4 | Создать WaveCompareService | `Services/Wave/WaveCompareService.cs` | 2.2, 2.3, 1.4, 1.5 |
| 2.5 | Unit-тесты для сопоставления | `Tests/Wave/FamilyMatcherTests.cs` | 2.2, 2.3 |

### Фаза 3: Валидация и обработка ошибок (1 день)

| # | Задача | Файл | Зависимости |
|---|--------|------|-------------|
| 3.1 | Создать WaveMappingValidator | `Services/Wave/WaveMappingValidator.cs` | 1.1 |
| 3.2 | Интегрировать валидацию в WaveCompareService | `Services/Wave/WaveCompareService.cs` | 2.4, 3.1 |
| 3.3 | Unit-тесты валидации | `Tests/Wave/ValidationTests.cs` | 3.1 |

### Фаза 4: Интеграция с CLI (1 день)

| # | Задача | Файл | Зависимости |
|---|--------|------|-------------|
| 4.1 | Добавить команду wave-compare | `Cli/Program.cs` | 2.4 |
| 4.2 | Создать JSON formatter для результата | `Services/Wave/WaveResultFormatter.cs` | 1.2 |
| 4.3 | Документация команды | `README.md` или `--help` | 4.1 |

### Фаза 5: Тестирование и оптимизация (1-2 дня)

| # | Задача | Файл | Зависимости |
|---|--------|------|-------------|
| 5.1 | Интеграционные тесты на реальных GEDCOM | `Tests/Wave/IntegrationTests.cs` | Все |
| 5.2 | Профилирование на больших деревьях | — | Все |
| 5.3 | Оптимизация узких мест | По результатам профилирования | 5.2 |
| 5.4 | Документация алгоритма | `.claude/WAVE_ALGORITHM.md` | Все |

---

## Структура файлов

```
GedcomGeniSync.Wave/
├── Models/
│   ├── Wave/
│   │   ├── TreeGraph.cs                 # Граф с индексами
│   │   ├── FamilyRecord.cs              # Запись о семье
│   │   ├── WaveCompareModels.cs         # PersonMapping, WaveCompareResult, etc.
│   │   └── FamilyMatchContext.cs        # Контекст сопоставления семьи
│   ├── PersonRecord.cs                  # (существующий)
│   ├── CompareModels.cs                 # (существующий)
│   └── ...
│
├── Services/
│   ├── Wave/
│   │   ├── TreeIndexer.cs               # Построение индексов
│   │   ├── TreeNavigator.cs             # Навигация по графу
│   │   ├── WaveCompareService.cs        # Главный оркестратор (BFS)
│   │   ├── FamilyMatcher.cs             # Поиск соответствующей семьи
│   │   ├── FamilyMemberMatcher.cs       # Сопоставление членов семьи
│   │   ├── ThresholdCalculator.cs       # Адаптивные пороги
│   │   ├── WaveMappingValidator.cs      # Валидация сопоставлений
│   │   └── WaveResultFormatter.cs       # Форматирование результата
│   │
│   ├── FuzzyMatcherService.cs           # (переиспользуем)
│   ├── NameVariantsService.cs           # (переиспользуем)
│   ├── GedcomLoader.cs                  # (переиспользуем)
│   └── ...
│
└── Interfaces/
    └── IWaveCompareService.cs           # Интерфейс для DI

GedcomGeniSync.Cli/
└── Program.cs                           # + команда wave-compare

GedcomGeniSync.Tests/
└── Wave/
    ├── TreeIndexerTests.cs
    ├── TreeNavigatorTests.cs
    ├── FamilyMatcherTests.cs
    ├── FamilyMemberMatcherTests.cs
    ├── WaveCompareServiceTests.cs
    ├── ValidationTests.cs
    └── IntegrationTests.cs
```

---

## Переиспользуемые компоненты

### Из существующей кодовой базы

| Компонент | Файл | Как используем |
|-----------|------|----------------|
| `FuzzyMatcherService` | `Services/FuzzyMatcherService.cs` | Для scoring персон в `FamilyMemberMatcher` |
| `NameVariantsService` | `Services/NameVariantsService.cs` | Транслитерация и эквиваленты имён |
| `GedcomLoader` | `Services/GedcomLoader.cs` | Загрузка GEDCOM файлов |
| `PersonRecord` | `Models/PersonRecord.cs` | Модель персоны |
| `DateInfo` | `Models/PersonRecord.cs` | Модель даты |
| `PersonFieldComparer` | `Services/Compare/PersonFieldComparer.cs` | Для определения различий полей |

### Методы FuzzyMatcherService для переиспользования

```csharp
// Основной метод сравнения двух персон
public int CalculateMatchScore(PersonRecord source, PersonRecord destination)

// Сравнение имён с учётом транслитерации
public int CompareNames(PersonRecord source, PersonRecord destination)

// Сравнение дат
public int CompareDates(DateInfo? source, DateInfo? dest)
```

### Возможные модификации FuzzyMatcherService

Для работы в контексте семьи может потребоваться добавить:

```csharp
/// <summary>
/// Упрощённое сравнение для контекста семьи (без учёта places, только имя + даты + пол)
/// </summary>
public int CalculateFamilyContextScore(PersonRecord source, PersonRecord destination)
{
    // Только критические поля для семейного контекста
    var nameScore = CompareNames(source, destination);
    var genderOk = source.Gender == destination.Gender ||
                   source.Gender == Gender.Unknown ||
                   destination.Gender == Gender.Unknown;

    if (!genderOk) return 0;

    var dateScore = CompareBirthYears(source.BirthYear, destination.BirthYear);

    return (int)(nameScore * 0.7 + dateScore * 0.3);
}
```

---

## CLI команда wave-compare

### Синтаксис

```bash
dotnet run -- wave-compare \
  --source <source.ged> \
  --destination <destination.ged> \
  --anchor-source <@I1@> \
  --anchor-destination <@I100@> \
  [--max-level <3>] \
  [--threshold-strategy <adaptive|aggressive|conservative|fixed>] \
  [--base-threshold <60>] \
  [--output <result.json>] \
  [--verbose]
```

### Параметры

| Параметр | Обязательный | По умолчанию | Описание |
|----------|--------------|--------------|----------|
| `--source` | Да | — | Путь к source GEDCOM файлу |
| `--destination` | Да | — | Путь к destination GEDCOM файлу |
| `--anchor-source` | Да | — | ID якорной персоны в source (например, @I1@) |
| `--anchor-destination` | Да | — | ID якорной персоны в destination |
| `--max-level` | Нет | 3 | Максимальный уровень распространения от якоря |
| `--threshold-strategy` | Нет | adaptive | Стратегия выбора порогов |
| `--base-threshold` | Нет | 60 | Базовый порог (для fixed стратегии) |
| `--output` | Нет | stdout | Путь для сохранения результата в JSON |
| `--verbose` | Нет | false | Подробный вывод (статистика по уровням) |

### Пример использования

```bash
# Базовое использование
dotnet run -- wave-compare \
  --source myheritage.ged \
  --destination geni_export.ged \
  --anchor-source @I1@ \
  --anchor-destination @I500@

# С параметрами
dotnet run -- wave-compare \
  --source tree1.ged \
  --destination tree2.ged \
  --anchor-source @I42@ \
  --anchor-destination @I1000@ \
  --max-level 5 \
  --threshold-strategy aggressive \
  --output comparison_result.json \
  --verbose
```

---

## Ожидаемые преимущества нового алгоритма

| Аспект | Текущий подход | Новый подход (Wave) |
|--------|----------------|---------------------|
| **Структура** | Итеративный, глобальный | BFS от якоря |
| **Контекст** | Fuzzy match по всему дереву | Сопоставление внутри семьи |
| **Пороги** | Фиксированные (70%) | Адаптивные (40-70%) |
| **Глубина** | Нечёткая, зависит от итераций | Чёткие уровни родственников |
| **Валидация** | Post-hoc, после всех итераций | По ходу распространения |
| **Понятность** | Сложная логика итераций | Интуитивный BFS |
| **Отлаживаемость** | Трудно понять путь сопоставления | Каждое сопоставление имеет путь |
| **Производительность** | O(N × M) сравнений | O(N) с индексами |

---

## Определение уровней родственников

### Вариант 1: По шагам связей (рекомендуется)

Каждый переход parent/child/spouse = 1 шаг

```
Уровень 0: Якорь
Уровень 1: Родители, супруги, дети, сиблинги
Уровень 2: Дедушки/бабушки, супруги детей, внуки, супруги сиблингов,
           дети сиблингов (племянники)
Уровень 3: Прадедушки, правнуки, двоюродные братья/сёстры, ...
```

### Вариант 2: По генетическому расстоянию

- Родитель/ребёнок = 1
- Супруг = 0 (или 0.5)
- Сиблинг = 2 (через общих родителей)

### Реализация (Вариант 1)

В BFS каждый переход увеличивает уровень на 1:
- От якоря к родителю: level + 1
- От якоря к супругу: level + 1
- От якоря к ребёнку: level + 1
- От якоря к сиблингу: level + 1 (через родительскую семью)

---

## Заметки по реализации

### Обработка особых случаев

1. **Несколько браков**: Персона может быть в нескольких семьях как супруг
2. **Неполные семьи**: Семья без мужа или без жены
3. **Большие семьи**: Более 10 детей — использовать венгерский алгоритм
4. **Циклы в дереве**: Невозможны в стандартном GEDCOM, но проверять visited
5. **Отсутствие семей**: Персона без семей (одиночка) — пропускаем

### Оптимизации

1. **Ленивое построение индексов**: Строить PersonsByBirthYear только если нужно
2. **Кэширование scores**: Если одна пара сравнивается несколько раз
3. **Early termination**: Если score уже ниже порога, не считать остальные поля
4. **Параллелизация**: Независимые ветки дерева можно обрабатывать параллельно

### Логирование

Для отладки важно логировать:
- Каждое новое сопоставление с путём
- Отклонённые сопоставления с причиной
- Статистику по каждому уровню
- Время обработки каждого уровня
