# Потенциальные улучшения алгоритма сравнения GEDCOM

Документ описывает нереализованные улучшения для `GedcomCompareService` и связанных сервисов.

---

## 1. Разрешение неоднозначных совпадений через контекст семьи

### Проблема

Когда `IndividualCompareService.FindBestMatch()` находит несколько кандидатов с одинаковым score, результат помечается как `AmbiguousMatch` и не сопоставляется.

**Пример:**
```
Источник: Иван Иванов (1950)

Destination:
  @I100@ Иван Иванов (1950)
  @I101@ Иван Иванов (1951)

Результат: AmbiguousMatch (score 95% у обоих)
```

### Предлагаемое решение

Использовать контекст семьи для разрешения неоднозначности:

```csharp
// В IndividualCompareService после обнаружения AmbiguousMatch
private MatchResult? TryResolveAmbiguousMatch(
    PersonRecord source,
    List<(PersonRecord person, int score)> candidates,
    IReadOnlyDictionary<string, string> existingMappings)
{
    // Найти семьи источника, где эта персона является членом
    var sourceFamilies = FindFamiliesContaining(source.Id);

    foreach (var candidate in candidates)
    {
        var destFamilies = FindFamiliesContaining(candidate.person.Id);

        // Проверить: есть ли у кандидата семья с уже сопоставленными членами?
        foreach (var destFam in destFamilies)
        {
            var mappedMembersCount = CountMappedMembers(destFam, existingMappings);
            if (mappedMembersCount >= 2)
            {
                // Этот кандидат уже в "известной" семье - выбрать его
                return new MatchResult
                {
                    MatchedPerson = candidate.person,
                    Score = candidate.score,
                    MatchedBy = "AmbiguousResolvedByFamily"
                };
            }
        }
    }

    return null; // Не удалось разрешить
}
```

### Приоритет: Высокий

Частый сценарий в реальных данных (однофамильцы, популярные имена).

---

## 2. Нечёткое сопоставление множественных детей в семье

### Проблема

Текущая логика `CaptureNewPersonMappings()` сопоставляет детей только если в обеих семьях ровно по одному немаппированному ребёнку:

```csharp
if (unmappedSourceChildren.Count == 1 && unmappedDestChildren.Count == 1)
    TryAddMapping(unmappedSourceChildren[0], unmappedDestChildren[0]);
```

Если немаппированных детей больше одного - ничего не происходит.

### Предлагаемое решение

Применить fuzzy matching к немаппированным детям внутри контекста семьи:

```csharp
private void TryMatchUnmappedChildren(
    Family sourceFamily,
    Family destFamily,
    IReadOnlyList<string> unmappedSourceChildren,
    IReadOnlyList<string> unmappedDestChildren,
    Dictionary<string, string> newMappings)
{
    if (unmappedSourceChildren.Count == 0 || unmappedDestChildren.Count == 0)
        return;

    // Если равное количество - попробовать сопоставить
    if (unmappedSourceChildren.Count == unmappedDestChildren.Count)
    {
        var matchMatrix = new List<(string srcId, string destId, double score)>();

        foreach (var srcChildId in unmappedSourceChildren)
        {
            var srcChild = GetPerson(srcChildId);
            foreach (var destChildId in unmappedDestChildren)
            {
                var destChild = GetPerson(destChildId);
                var matchResult = _fuzzyMatcher.Compare(srcChild, destChild);
                matchMatrix.Add((srcChildId, destChildId, matchResult.Score));
            }
        }

        // Жадный алгоритм: выбрать лучшие пары без конфликтов
        var usedSource = new HashSet<string>();
        var usedDest = new HashSet<string>();

        foreach (var match in matchMatrix.OrderByDescending(m => m.score))
        {
            if (match.score < 70) // Минимальный порог для детей в той же семье
                break;

            if (!usedSource.Contains(match.srcId) && !usedDest.Contains(match.destId))
            {
                newMappings[match.srcId] = match.destId;
                usedSource.Add(match.srcId);
                usedDest.Add(match.destId);
            }
        }
    }
    // Если неравное количество - сопоставить только уверенные пары (score >= 85)
    else
    {
        // Аналогичная логика, но с более высоким порогом
    }
}
```

### Дополнительно: Учёт порядка рождения

```csharp
// Бонус за совпадение порядка среди детей
private double GetBirthOrderBonus(
    string sourceChildId,
    string destChildId,
    Family sourceFamily,
    Family destFamily)
{
    var sourceIndex = sourceFamily.ChildIds.IndexOf(sourceChildId);
    var destIndex = destFamily.ChildIds.IndexOf(destChildId);

    if (sourceIndex == destIndex)
        return 5.0; // +5 баллов за совпадение позиции

    if (Math.Abs(sourceIndex - destIndex) == 1)
        return 2.0; // +2 балла за соседнюю позицию

    return 0;
}
```

### Приоритет: Высокий

Часто встречаются семьи с несколькими детьми.

---

## 3. Транзитивное сопоставление через братьев/сестёр (Siblings)

### Проблема

Текущий алгоритм не использует связи между братьями/сёстрами для выявления новых сопоставлений.

**Пример:**
```
@I1@ (Отец) → @I100@ (сопоставлен)
@I2@ (Мать) → @I101@ (сопоставлен)
@I3@ (Сын)  → @I102@ (сопоставлен)
@I4@ (Дочь) → ???    (не сопоставлена)

@I4@ - сестра @I3@, но это не используется
```

### Предлагаемое решение

Добавить этап "Транзитивные связи" в итеративный цикл:

```csharp
private Dictionary<string, string> FindTransitiveMappingsThroughSiblings(
    IReadOnlyDictionary<string, string> existingMappings,
    Dictionary<string, Family> sourceFamilies,
    Dictionary<string, Family> destFamilies)
{
    var newMappings = new Dictionary<string, string>();

    // Для каждой сопоставленной персоны
    foreach (var (sourceId, destId) in existingMappings)
    {
        var sourcePerson = GetPerson(sourceId);
        var destPerson = GetPerson(destId);

        // Найти братьев/сестёр в источнике
        var sourceSiblings = FindSiblings(sourceId, sourceFamilies);
        var destSiblings = FindSiblings(destId, destFamilies);

        // Отфильтровать уже сопоставленных
        var unmappedSourceSiblings = sourceSiblings
            .Where(id => !existingMappings.ContainsKey(id))
            .ToList();
        var unmappedDestSiblings = destSiblings
            .Where(id => !existingMappings.Values.Contains(id))
            .ToList();

        // Попробовать сопоставить
        if (unmappedSourceSiblings.Count == 1 && unmappedDestSiblings.Count == 1)
        {
            // Простой случай: один брат/сестра с каждой стороны
            var srcSibling = GetPerson(unmappedSourceSiblings[0]);
            var destSibling = GetPerson(unmappedDestSiblings[0]);

            var matchScore = _fuzzyMatcher.Compare(srcSibling, destSibling).Score;
            if (matchScore >= 60) // Пониженный порог - контекст известен
            {
                newMappings[unmappedSourceSiblings[0]] = unmappedDestSiblings[0];
            }
        }
        else if (unmappedSourceSiblings.Count > 0 && unmappedDestSiblings.Count > 0)
        {
            // Сложный случай: несколько братьев/сестёр
            TryMatchUnmappedSiblings(unmappedSourceSiblings, unmappedDestSiblings, newMappings);
        }
    }

    return newMappings;
}

private List<string> FindSiblings(string personId, Dictionary<string, Family> families)
{
    var siblings = new HashSet<string>();

    // Найти все семьи, где персона - ребёнок
    var familiesAsChild = families.Values
        .Where(f => f.ChildIds.Contains(personId));

    foreach (var family in familiesAsChild)
    {
        foreach (var childId in family.ChildIds)
        {
            if (childId != personId)
                siblings.Add(childId);
        }
    }

    return siblings.ToList();
}
```

### Приоритет: Средний

Полезно для больших семей с неполными данными.

---

## 4. Приоритизация семей по количеству известных членов

### Проблема

Сейчас семьи обрабатываются в произвольном порядке (порядок словаря). Это неоптимально:

```
Семья A: 4 из 5 членов маппированы → высокая вероятность успеха
Семья B: 1 из 6 членов маппирован  → низкая вероятность

Если обработать B первой, можем получить ошибочные сопоставления
```

### Предлагаемое решение

Сортировать семьи по "уверенности" перед обработкой:

```csharp
private IEnumerable<Family> PrioritizeFamilies(
    IEnumerable<Family> families,
    IReadOnlyDictionary<string, string> existingMappings)
{
    return families
        .Select(f => new
        {
            Family = f,
            MappedCount = CountMappedMembers(f, existingMappings),
            TotalCount = GetTotalMemberCount(f),
            Confidence = CalculateConfidence(f, existingMappings)
        })
        .OrderByDescending(x => x.Confidence)
        .ThenByDescending(x => x.MappedCount)
        .Select(x => x.Family);
}

private double CalculateConfidence(Family family, IReadOnlyDictionary<string, string> mappings)
{
    var total = GetTotalMemberCount(family);
    var mapped = CountMappedMembers(family, mappings);

    if (total == 0) return 0;

    var ratio = (double)mapped / total;

    // Бонус если оба супруга маппированы
    var bothSpousesMapped =
        (family.HusbandId == null || mappings.ContainsKey(family.HusbandId)) &&
        (family.WifeId == null || mappings.ContainsKey(family.WifeId));

    if (bothSpousesMapped)
        ratio += 0.2;

    return Math.Min(ratio, 1.0);
}
```

### Приоритет: Средний

Улучшает качество на больших деревьях.

---

## 5. Валидация и откат ошибочных сопоставлений

### Проблема

Если на ранней итерации произошло ошибочное сопоставление, оно распространяется на последующие итерации и может привести к каскаду ошибок.

**Пример:**
```
Итерация 1: @I5@ ошибочно сопоставлен с @I500@
Итерация 2: Семья @I5@ находит "совпадение" в destination
Итерация 3: Дети @I5@ сопоставляются с детьми @I500@
...
Результат: Целая ветка дерева сопоставлена неверно
```

### Предлагаемое решение

#### 5.1 Пост-валидация сопоставлений

```csharp
private ValidationResult ValidateMappings(
    IReadOnlyDictionary<string, string> mappings,
    Dictionary<string, Family> sourceFamilies,
    Dictionary<string, Family> destFamilies)
{
    var issues = new List<MappingIssue>();

    foreach (var (sourceId, destId) in mappings)
    {
        var sourcePerson = GetPerson(sourceId);
        var destPerson = GetPerson(destId);

        // Проверка 1: Пол должен совпадать (если указан)
        if (sourcePerson.Gender != null && destPerson.Gender != null &&
            sourcePerson.Gender != destPerson.Gender)
        {
            issues.Add(new MappingIssue
            {
                SourceId = sourceId,
                DestId = destId,
                Type = IssueType.GenderMismatch,
                Severity = IssueSeverity.High
            });
        }

        // Проверка 2: Даты не должны противоречить
        if (HasDateContradiction(sourcePerson, destPerson))
        {
            issues.Add(new MappingIssue
            {
                SourceId = sourceId,
                DestId = destId,
                Type = IssueType.DateContradiction,
                Severity = IssueSeverity.Medium
            });
        }

        // Проверка 3: Семейные роли должны быть консистентны
        var roleIssues = ValidateFamilyRoles(sourceId, destId, mappings, sourceFamilies, destFamilies);
        issues.AddRange(roleIssues);
    }

    return new ValidationResult { Issues = issues };
}

private bool HasDateContradiction(PersonRecord source, PersonRecord dest)
{
    // Смерть раньше рождения?
    // Рождение ребёнка до рождения родителя?
    // Разница в датах рождения > 5 лет?

    if (source.BirthDate?.Date != null && dest.BirthDate?.Date != null)
    {
        var yearDiff = Math.Abs(
            source.BirthDate.Date.Value.Year -
            dest.BirthDate.Date.Value.Year);

        if (yearDiff > 5)
            return true;
    }

    return false;
}
```

#### 5.2 Механизм отката

```csharp
private Dictionary<string, string> RollbackSuspiciousMappings(
    Dictionary<string, string> mappings,
    ValidationResult validation)
{
    var toRemove = new HashSet<string>();

    // Удалить сопоставления с высокой серьёзностью проблем
    foreach (var issue in validation.Issues.Where(i => i.Severity == IssueSeverity.High))
    {
        toRemove.Add(issue.SourceId);

        // Также удалить зависимые сопоставления (дети, супруги)
        var dependentIds = FindDependentMappings(issue.SourceId, mappings);
        foreach (var depId in dependentIds)
        {
            toRemove.Add(depId);
        }
    }

    return mappings
        .Where(kvp => !toRemove.Contains(kvp.Key))
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
}
```

#### 5.3 Уровни уверенности

```csharp
public record MappingEntry
{
    public string SourceId { get; init; }
    public string DestId { get; init; }
    public double Confidence { get; init; }  // 0.0 - 1.0
    public string MatchedBy { get; init; }   // "RFN", "Fuzzy", "Family", etc.
    public int IterationFound { get; init; }
}

// В GedcomCompareService
private Dictionary<string, MappingEntry> _mappingsWithConfidence;

// При добавлении нового сопоставления
private void AddMappingWithConfidence(string sourceId, string destId, double score, string method)
{
    var confidence = CalculateConfidence(score, method);

    _mappingsWithConfidence[sourceId] = new MappingEntry
    {
        SourceId = sourceId,
        DestId = destId,
        Confidence = confidence,
        MatchedBy = method,
        IterationFound = _currentIteration
    };
}

private double CalculateConfidence(double score, string method)
{
    return method switch
    {
        "RFN" => 1.0,                    // 100% уверенность
        "Fuzzy" => score / 100.0,        // Пропорционально score
        "Family_SingleChild" => 0.85,    // Высокая, но не абсолютная
        "Family_FuzzyChild" => 0.70,     // Средняя
        "Sibling_Transitive" => 0.65,    // Средняя
        _ => 0.50
    };
}
```

### Приоритет: Высокий

Критически важно для предотвращения каскадных ошибок.

---

## 6. Обработка полигамных/повторных браков

### Проблема

Некоторые GEDCOM файлы содержат несколько FAM записей для одного человека (несколько браков). Текущая логика может неправильно сопоставить семьи.

**Пример:**
```
Source:
  @F1@: HUSB=@I1@, WIFE=@I2@, CHIL={@I3@}  (первый брак)
  @F2@: HUSB=@I1@, WIFE=@I4@, CHIL={@I5@}  (второй брак)

Destination:
  @F10@: HUSB=@I100@, WIFE=@I101@, CHIL={@I102@}
  @F11@: HUSB=@I100@, WIFE=@I103@, CHIL={@I104@}
```

### Предлагаемое решение

```csharp
private List<Family> FindMatchingFamiliesForPolygamy(
    Family sourceFamily,
    IEnumerable<Family> destFamilies,
    IReadOnlyDictionary<string, string> mappings)
{
    var candidates = new List<(Family family, double score)>();

    // Маппированный ID супруга
    var mappedHusbandId = sourceFamily.HusbandId != null &&
        mappings.TryGetValue(sourceFamily.HusbandId, out var hId) ? hId : null;
    var mappedWifeId = sourceFamily.WifeId != null &&
        mappings.TryGetValue(sourceFamily.WifeId, out var wId) ? wId : null;

    foreach (var destFamily in destFamilies)
    {
        var score = 0.0;

        // Совпадение мужа
        if (mappedHusbandId != null && destFamily.HusbandId == mappedHusbandId)
            score += 40;

        // Совпадение жены
        if (mappedWifeId != null && destFamily.WifeId == mappedWifeId)
            score += 40;

        // Совпадение детей
        var childScore = CalculateChildrenOverlap(sourceFamily, destFamily, mappings);
        score += childScore * 20;

        // Совпадение даты брака (если есть)
        if (DatesMatch(sourceFamily.MarriageDate, destFamily.MarriageDate))
            score += 10;

        if (score > 30) // Минимальный порог
            candidates.Add((destFamily, score));
    }

    // Вернуть лучшего кандидата или все кандидаты для ручного разрешения
    return candidates
        .OrderByDescending(c => c.score)
        .Select(c => c.family)
        .ToList();
}
```

### Приоритет: Низкий

Редкий сценарий, но важен для полноты.

---

## 7. Параллельная обработка больших деревьев

### Проблема

Для деревьев с тысячами персон последовательная обработка может быть медленной.

### Предлагаемое решение

```csharp
private async Task<IndividualCompareResult> CompareIndividualsParallel(
    IReadOnlyDictionary<string, PersonRecord> sourcePersons,
    IReadOnlyDictionary<string, PersonRecord> destPersons,
    CompareOptions options,
    IReadOnlyDictionary<string, string> existingMappings)
{
    var results = new ConcurrentBag<PersonMatchResult>();

    // Разделить на батчи
    var batches = sourcePersons.Values
        .Where(p => !existingMappings.ContainsKey(p.Id))
        .Chunk(100);

    await Parallel.ForEachAsync(batches, async (batch, ct) =>
    {
        foreach (var person in batch)
        {
            var match = FindBestMatch(person, destPersons, options);
            results.Add(new PersonMatchResult { Source = person, Match = match });
        }
    });

    // Собрать результаты
    return BuildResult(results, existingMappings);
}
```

### Приоритет: Низкий

Оптимизация производительности, не влияет на качество.

---

## 8. Улучшенная диагностика и логирование

### Проблема

Сложно понять, почему определённые персоны не были сопоставлены.

### Предлагаемое решение

```csharp
public record MatchAttemptLog
{
    public string SourceId { get; init; }
    public string SourceName { get; init; }
    public List<CandidateLog> Candidates { get; init; }
    public string FinalDecision { get; init; }  // "Matched", "Ambiguous", "NoMatch"
    public string Reason { get; init; }
}

public record CandidateLog
{
    public string DestId { get; init; }
    public string DestName { get; init; }
    public double Score { get; init; }
    public List<ScoreBreakdown> Breakdown { get; init; }
    public bool WasSelected { get; init; }
    public string RejectionReason { get; init; }
}

public record ScoreBreakdown
{
    public string Field { get; init; }      // "FirstName", "BirthDate", etc.
    public double Points { get; init; }
    public double MaxPoints { get; init; }
    public string SourceValue { get; init; }
    public string DestValue { get; init; }
}
```

Добавить в `CompareResult`:
```csharp
public record CompareResult
{
    // ... существующие поля ...

    public ImmutableList<MatchAttemptLog> MatchingDiagnostics { get; init; }
}
```

### Приоритет: Средний

Полезно для отладки и понимания результатов.

---

## Порядок реализации (рекомендуемый)

| # | Улучшение | Приоритет | Сложность | Влияние |
|---|-----------|-----------|-----------|---------|
| 1 | Валидация и откат ошибок | Высокий | Средняя | Высокое |
| 2 | Разрешение ambiguous через семьи | Высокий | Низкая | Высокое |
| 3 | Нечёткое сопоставление детей | Высокий | Средняя | Высокое |
| 4 | Приоритизация семей | Средний | Низкая | Среднее |
| 5 | Транзитивные связи (siblings) | Средний | Средняя | Среднее |
| 6 | Улучшенная диагностика | Средний | Низкая | Среднее |
| 7 | Полигамные браки | Низкий | Средняя | Низкое |
| 8 | Параллельная обработка | Низкий | Высокая | Низкое |

---

## Метрики успеха

После реализации улучшений ожидаемые результаты:

| Метрика | Текущее | Ожидаемое |
|---------|---------|-----------|
| Точность сопоставления | ~85% | ~95% |
| Ambiguous matches | ~10% | ~3% |
| Ложные сопоставления | ~5% | ~1% |
| Каскадные ошибки | Возможны | Минимизированы |
