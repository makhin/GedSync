# План исправления логики добавления новых людей и семейных связей

## Проблема

При добавлении новых людей в Geni не обеспечивается целостность семейных связей. Текущая реализация добавляет связь только с **одним** родственником, хотя в источнике (GEDCOM) человек может быть связан с несколькими людьми одновременно.

---

## Анализ текущего кода

### 1. Проблема в `WaveCompareCommandHandler.cs` (строки 321-359)

```csharp
private static (string RelatedSourceId, CompareRelationType RelationType)?
FindHighConfidenceRelation(PersonRecord person, ...)
{
    // Возвращает ПЕРВОГО найденного, остальные игнорируются!
    if (HasHighConfidence(person.FatherId))
        return (person.FatherId!, CompareRelationType.Child);  // ← ВЫХОД

    if (HasHighConfidence(person.MotherId))
        return (person.MotherId!, CompareRelationType.Child);  // ← НИКОГДА НЕ ДОСТИГАЕТСЯ
    ...
}
```

**Результат:** Если оба родителя сопоставлены с высокой уверенностью, возвращается только отец.

### 2. Ограничение модели `NodeToAdd` (`CompareModels.cs:273-292`)

```csharp
public record NodeToAdd
{
    public string? RelatedToNodeId { get; init; }     // ТОЛЬКО ОДИН!
    public CompareRelationType? RelationType { get; init; }
}
```

**Результат:** Модель архитектурно ограничена одним родственником.

### 3. Ограничение `AddExecutor` (`AddExecutor.cs:148-178`)

Добавляет профиль только через один API вызов:
- `AddChildAsync(parentId, ...)` — ребёнок связывается только с одним родителем
- `AddPartnerAsync(profileId, ...)` — партнёр добавляется, но дети не связываются
- `AddParentAsync(childId, ...)` — родитель добавляется, но связь супругов не создаётся

---

## Сценарии, требующие исправления

### Сценарий 1: Добавление ребёнка с двумя родителями

**Исходные данные в GEDCOM:**
```
Семья F1: Муж (Иван) + Жена (Мария) = Дети: Петр
Иван (@I1@) → сопоставлен с Geni (score=95)
Мария (@I2@) → сопоставлена с Geni (score=92)
Пётр (@I3@) → НЕ сопоставлен (нужно добавить)
```

**Текущее поведение:**
1. `FindHighConfidenceRelation` находит Ивана первым
2. Пётр добавляется через `AddChildAsync(Иван_geni_id, Пётр_данные)`
3. Мария **НЕ получает** связь с Петром

**Ожидаемое поведение:**
- Пётр должен быть связан с обоими родителями
- Использовать `AddChildToUnionAsync(union_id, ...)` если у Ивана и Марии есть общий union в Geni

---

### Сценарий 2: Добавление партнёра с существующими детьми

**Исходные данные в GEDCOM:**
```
Семья F1: Муж (Иван) + Жена (Мария) = Дети: Петр, Анна
Иван (@I1@) → сопоставлен с Geni
Пётр (@I3@) → сопоставлен с Geni (только как ребёнок Ивана)
Анна (@I4@) → сопоставлена с Geni (только как ребёнок Ивана)
Мария (@I2@) → НЕ сопоставлена (нужно добавить)
```

**Текущее поведение:**
1. Мария добавляется через `AddPartnerAsync(Иван_geni_id, Мария_данные)`
2. Пётр и Анна остаются без связи с Марией (у них только отец)

**Ожидаемое поведение:**
- После добавления Марии нужно проверить детей в GEDCOM
- Для каждого ребёнка, который есть у обоих родителей в GEDCOM и уже в Geni, нужно добавить связь мать-ребёнок

---

### Сценарий 3: Добавление родителей ребёнку (супруги)

**Исходные данные в GEDCOM:**
```
Семья F1: Муж (Иван) + Жена (Мария) = Дети: Петр
Пётр (@I3@) → сопоставлен с Geni
Иван (@I1@) → НЕ сопоставлен (нужно добавить)
Мария (@I2@) → НЕ сопоставлена (нужно добавить)
```

**Текущее поведение:**
1. Иван добавляется через `AddParentAsync(Пётр_geni_id, Иван_данные)`
2. Мария добавляется через `AddParentAsync(Пётр_geni_id, Мария_данные)`
3. Иван и Мария **НЕ связаны** как супруги в Geni

**Ожидаемое поведение:**
- Если в GEDCOM Иван и Мария — супруги (в одной семье FAM)
- Нужно создать связь супругов между ними в Geni
- Либо добавить через `AddPartnerToUnionAsync`

---

### Сценарий 4: Добавление сиблинга

**Исходные данные в GEDCOM:**
```
Семья F1: Муж (Иван) + Жена (Мария) = Дети: Петр, Анна
Пётр (@I3@) → сопоставлен с Geni
Анна (@I4@) → НЕ сопоставлена (нужно добавить как сиблинга Петра)
```

**Текущее поведение:**
1. Анна может быть добавлена как сиблинг Петра
2. Но связи с родителями (Иван, Мария) могут быть потеряны

**Ожидаемое поведение:**
- При добавлении сиблинга проверить общих родителей
- Убедиться, что связи с родителями созданы

---

## Предлагаемые решения

### Решение A: Расширение модели `NodeToAdd`

**Файл:** `GedcomGeniSync.Core/Models/CompareModels.cs`

```csharp
public record NodeToAdd
{
    public required string SourceId { get; init; }
    public required PersonData PersonData { get; init; }

    // Основная связь (оставляем для обратной совместимости)
    public string? RelatedToNodeId { get; init; }
    public CompareRelationType? RelationType { get; init; }

    // НОВОЕ: Дополнительные связи
    public ImmutableList<AdditionalRelation> AdditionalRelations { get; init; }
        = ImmutableList<AdditionalRelation>.Empty;

    // НОВОЕ: ID семьи в источнике (для поиска union в destination)
    public string? SourceFamilyId { get; init; }

    public int DepthFromExisting { get; init; }
}

public record AdditionalRelation
{
    public required string RelatedToNodeId { get; init; }
    public required CompareRelationType RelationType { get; init; }
}
```

---

### Решение B: Изменение `FindHighConfidenceRelation`

**Файл:** `GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs`

Переименовать в `FindHighConfidenceRelations` (множественное число) и возвращать все связи:

```csharp
private static ImmutableList<RelationInfo> FindHighConfidenceRelations(
    PersonRecord person,
    IReadOnlyDictionary<string, PersonMapping> mappingBySource,
    int confidenceThreshold)
{
    var relations = ImmutableList.CreateBuilder<RelationInfo>();

    bool HasHighConfidence(string? relativeId) =>
        !string.IsNullOrWhiteSpace(relativeId)
        && mappingBySource.TryGetValue(relativeId!, out var mapping)
        && mapping.MatchScore >= confidenceThreshold;

    // Собираем ВСЕХ родителей
    if (HasHighConfidence(person.FatherId))
        relations.Add(new RelationInfo(person.FatherId!, CompareRelationType.Child));

    if (HasHighConfidence(person.MotherId))
        relations.Add(new RelationInfo(person.MotherId!, CompareRelationType.Child));

    // Супруги
    foreach (var spouseId in person.SpouseIds.Where(HasHighConfidence))
        relations.Add(new RelationInfo(spouseId, CompareRelationType.Spouse));

    // Дети
    foreach (var childId in person.ChildrenIds.Where(HasHighConfidence))
        relations.Add(new RelationInfo(childId, CompareRelationType.Parent));

    // Сиблинги (только если нет других связей)
    if (relations.Count == 0)
    {
        foreach (var siblingId in person.SiblingIds.Where(HasHighConfidence))
            relations.Add(new RelationInfo(siblingId, CompareRelationType.Sibling));
    }

    return relations.ToImmutable();
}

public record RelationInfo(string RelatedSourceId, CompareRelationType RelationType);
```

---

### Решение C: Использование Union API (РЕКОМЕНДУЕТСЯ)

**Ключевая идея:** При добавлении ребёнка с двумя родителями — найти их общий union в Geni и использовать `AddChildToUnionAsync`.

**Изменения в `AddExecutor.cs`:**

```csharp
case CompareRelationType.Child:
    // Проверяем, есть ли второй родитель
    var secondParentGeniId = node.AdditionalRelations
        .Where(r => r.RelationType == CompareRelationType.Child)
        .Select(r => profileMap.GetValueOrDefault(r.RelatedToNodeId))
        .FirstOrDefault();

    if (!string.IsNullOrEmpty(secondParentGeniId))
    {
        // Есть оба родителя — ищем их общий union
        var union = await FindCommonUnionAsync(cleanGeniId, secondParentGeniId);
        if (union != null)
        {
            _logger.LogInformation("  Adding child to union {UnionId}", union.Id);
            createdProfile = await _profileClient.AddChildToUnionAsync(union.Id, profileCreate);
            break;
        }
    }

    // Fallback: добавляем к одному родителю
    _logger.LogInformation("  Adding as child to {GeniId}", cleanGeniId);
    createdProfile = await _profileClient.AddChildAsync(cleanGeniId, profileCreate);
    break;
```

**Новый метод для поиска общего union:**

```csharp
private async Task<GeniUnion?> FindCommonUnionAsync(string profile1Id, string profile2Id)
{
    var family1 = await _profileClient.GetImmediateFamilyAsync(profile1Id);
    if (family1?.Unions == null) return null;

    foreach (var unionId in family1.Unions.Keys)
    {
        var union = family1.Unions[unionId];
        var partnerIds = union.Partners?.Select(p => p.Id).ToList() ?? new List<string>();

        if (partnerIds.Contains(profile1Id) && partnerIds.Contains(profile2Id))
        {
            return union;
        }
    }

    return null;
}
```

---

### Решение D: Пост-обработка связей

**Идея:** После добавления всех профилей — второй проход для добавления недостающих связей.

**Новый метод `LinkMissingRelationsAsync`:**

```csharp
public async Task LinkMissingRelationsAsync(
    List<NodeToAdd> addedNodes,
    Dictionary<string, string> createdProfiles,
    GedcomLoadResult gedcom)
{
    foreach (var node in addedNodes)
    {
        if (!createdProfiles.TryGetValue(node.SourceId, out var geniId))
            continue;

        var sourcePerson = gedcom.Persons[node.SourceId];

        // Проверяем связь с матерью
        if (!string.IsNullOrEmpty(sourcePerson.MotherId)
            && createdProfiles.TryGetValue(sourcePerson.MotherId, out var motherGeniId))
        {
            // TODO: Добавить связь мать-ребёнок, если её нет
            await EnsureParentChildLinkAsync(motherGeniId, geniId);
        }

        // Проверяем связь с отцом
        if (!string.IsNullOrEmpty(sourcePerson.FatherId)
            && createdProfiles.TryGetValue(sourcePerson.FatherId, out var fatherGeniId))
        {
            await EnsureParentChildLinkAsync(fatherGeniId, geniId);
        }

        // Проверяем связи супругов
        foreach (var spouseId in sourcePerson.SpouseIds)
        {
            if (createdProfiles.TryGetValue(spouseId, out var spouseGeniId))
            {
                await EnsureSpouseLinkAsync(geniId, spouseGeniId);
            }
        }
    }
}
```

---

## Рекомендуемый план реализации

### Фаза 1: Расширение модели (минимальные изменения)

1. **Расширить `NodeToAdd`:**
   - Добавить `AdditionalRelations` для хранения дополнительных связей
   - Добавить `SourceFamilyId` для контекста семьи

2. **Изменить `FindHighConfidenceRelation` → `FindHighConfidenceRelations`:**
   - Собирать ВСЕ высокоуверенные связи, не только первую
   - Определять приоритет: супруги → родители → дети → сиблинги

3. **Обновить `BuildWaveReport`:**
   - Использовать новый метод поиска связей
   - Заполнять `AdditionalRelations` в `NodeToAdd`

### Фаза 2: Улучшение AddExecutor

4. **Обработка случая двух родителей:**
   - При добавлении ребёнка проверять `AdditionalRelations`
   - Если есть второй родитель — искать их общий union
   - Использовать `AddChildToUnionAsync` когда возможно

5. **Обработка добавления супруга:**
   - После добавления супруга проверять общих детей
   - Для каждого ребёнка, который есть у обоих в GEDCOM и уже в Geni — добавлять связь

6. **Обработка добавления родителей:**
   - Если добавляем двух родителей, которые супруги в GEDCOM
   - После добавления второго — создавать связь супругов

### Фаза 3: Пост-обработка (опционально)

7. **Добавить второй проход `LinkMissingRelationsAsync`:**
   - После всех добавлений проверять недостающие связи
   - Добавлять недостающие связи родитель-ребёнок
   - Добавлять недостающие связи супругов

---

## Файлы для изменения

| Файл | Изменения |
|------|-----------|
| `GedcomGeniSync.Core/Models/CompareModels.cs` | Добавить `AdditionalRelations`, `SourceFamilyId`, `AdditionalRelation` record |
| `GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs` | Изменить `FindHighConfidenceRelation` → `FindHighConfidenceRelations`, обновить `BuildWaveReport` |
| `GedcomGeniSync.Cli/Services/AddExecutor.cs` | Добавить логику для union API, пост-обработку связей |
| `GedcomGeniSync.ApiClient/Services/Interfaces/IGeniProfileClient.cs` | (уже есть `AddChildToUnionAsync`) |

---

## Тесты

### Новые тесты для WaveCompareCommandHandler

1. `FindHighConfidenceRelations_BothParentsMapped_ReturnsBothParents`
2. `FindHighConfidenceRelations_OnlyFatherMapped_ReturnsFatherOnly`
3. `FindHighConfidenceRelations_MultipleSpouses_ReturnsAllSpouses`

### Новые тесты для AddExecutor

1. `ExecuteAdditionsAsync_ChildWithBothParents_UsesUnionApi`
2. `ExecuteAdditionsAsync_AddPartner_LinksExistingChildren`
3. `ExecuteAdditionsAsync_AddTwoParents_CreatesSpouseLink`

---

## Приоритеты

| Приоритет | Сценарий | Влияние |
|-----------|----------|---------|
| **P0 (Критический)** | Ребёнок с двумя родителями | Часто встречается, нарушает структуру семьи |
| **P1 (Высокий)** | Добавление супруга с детьми | Часто встречается |
| **P2 (Средний)** | Добавление родителей-супругов | Реже встречается |
| **P3 (Низкий)** | Сиблинги с родителями | Редко используется |

---

## Оценка трудозатрат

- **Фаза 1 (модель + поиск связей):** 2-3 часа
- **Фаза 2 (AddExecutor + Union API):** 3-4 часа
- **Фаза 3 (пост-обработка):** 2-3 часа
- **Тесты:** 2-3 часа

**Итого:** ~10-13 часов разработки

---

## Риски

1. **API лимиты Geni:** Дополнительные вызовы `GetImmediateFamilyAsync` для поиска union могут увеличить количество запросов
2. **Порядок добавления:** Если оба родителя ещё не добавлены в Geni, union не существует — нужно обрабатывать fallback
3. **Обратная совместимость:** Изменения в `NodeToAdd` могут сломать существующие сериализованные отчёты

---

## Следующие шаги

1. [ ] Согласовать план с заказчиком
2. [ ] Реализовать Фазу 1 (расширение модели)
3. [ ] Написать тесты для нового поведения
4. [ ] Реализовать Фазу 2 (AddExecutor)
5. [ ] Провести интеграционное тестирование
6. [ ] Реализовать Фазу 3 (если требуется)
