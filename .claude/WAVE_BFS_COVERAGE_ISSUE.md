# Wave BFS Coverage Issue - Пропуск несопоставленных членов семьи

## Дата: 2025-12-14

## Проблема

Волновой алгоритм **не обрабатывает потомков и родственников** людей, которые:
1. Были найдены в семье
2. Но **не были сопоставлены** (score < threshold)

### Пример проблемного сценария

```
Level 1: Александр Иванович Рызванович (@I27@) [СОПОСТАВЛЕН]

Семья @F12@:
  Husband: Александр Рызванович (@I27@) [уже сопоставлен]
  Wife: Вера Николаева (@I34@) [сопоставлена, score: 100%]
  Child: Юрий Рызванович (@I35@) [НЕ сопоставлен - score < threshold]

Семья Юрия @F13@:
  Husband: Юрий Рызванович (@I35@)
  Wife: Жена Юрия
  Children: Дети Юрия [3-4 человека]

Потомки детей Юрия: [10-15 человек]
```

**Текущее поведение:**
- ✅ Семья F12 найдена (муж уже сопоставлен)
- ✅ Вера (@I34@) сопоставлена → добавлена в queue
- ❌ Юрий (@I35@) НЕ сопоставлен (score < threshold) → **НЕ добавлен в queue**
- ❌ Семья F13 (семья Юрия) **НИКОГДА не будет обработана**
- ❌ Потомки Юрия (15-20 человек) **НИКОГДА не будут обработаны**

**Результат**: Теряем целые ветки дерева!

## Корневая причина

### Текущая логика в WaveCompareService.cs (строки 192-245)

```csharp
if (destFamily != null)  // Семья найдена
{
    var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

    foreach (var mapping in newMappings)
    {
        if (!processed.Contains(mapping.SourceId))
        {
            var validationResult = _validator.ValidateMapping(...);

            if (validationResult.IsValid)  // ← ПРОБЛЕМА ЗДЕСЬ
            {
                mappings[mapping.SourceId] = mapping;
                queue.Enqueue((mapping.SourceId, level + 1));  // ← Добавляем ТОЛЬКО если сопоставлен
                processed.Add(mapping.SourceId);
            }
            else
            {
                // НЕ добавляем в очередь - персона потеряна!
            }
        }
    }
}
```

**Проблема**: В очередь попадают ТОЛЬКО успешно сопоставленные персоны.

Несопоставленные члены семьи:
- НЕ добавляются в очередь
- НЕ обрабатываются на следующих уровнях
- Их семьи и потомки теряются

## Решение: Добавлять всех членов найденной семьи в очередь

### Идея

Если семья была найдена, **все её несопоставленные члены должны быть добавлены в очередь** для обработки на следующих уровнях, даже если:
- Их score < threshold
- Валидация не прошла
- Подходящий кандидат не найден

**Обоснование:**
1. Семья найдена → структурный контекст подтвержден
2. Даже если человека не удалось сопоставить здесь, у него могут быть:
   - Свои семьи (как супруг/родитель)
   - Потомки
   - Другие родственники
3. Эти родственники могут быть успешно сопоставлены
4. Через них можно вернуться к исходной персоне с лучшим контекстом

### Новая логика

```csharp
if (destFamily != null)  // Семья найдена
{
    // 1. Пытаемся сопоставить членов семьи
    var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

    // 2. Собираем всех членов source семьи
    var allSourceFamilyMembers = new HashSet<string>();
    if (sourceFamily.HusbandId != null) allSourceFamilyMembers.Add(sourceFamily.HusbandId);
    if (sourceFamily.WifeId != null) allSourceFamilyMembers.Add(sourceFamily.WifeId);
    foreach (var childId in sourceFamily.ChildIds)
        allSourceFamilyMembers.Add(childId);

    // 3. Добавляем сопоставленных членов
    foreach (var mapping in newMappings)
    {
        if (!processed.Contains(mapping.SourceId))
        {
            var validationResult = _validator.ValidateMapping(...);

            if (validationResult.IsValid)
            {
                mappings[mapping.SourceId] = mapping;
                queue.Enqueue((mapping.SourceId, level + 1));
                processed.Add(mapping.SourceId);
                allSourceFamilyMembers.Remove(mapping.SourceId); // ← Убираем из списка несопоставленных
            }
        }
        else
        {
            allSourceFamilyMembers.Remove(mapping.SourceId); // Уже обработан ранее
        }
    }

    // 4. Добавляем НЕСОПОСТАВЛЕННЫХ членов семьи в очередь для "исследования"
    foreach (var unmatchedMemberId in allSourceFamilyMembers)
    {
        if (!processed.Contains(unmatchedMemberId))
        {
            queue.Enqueue((unmatchedMemberId, level + 1));
            processed.Add(unmatchedMemberId);

            _logger.LogDebug(
                "Added unmatched family member {SourceId} to queue for exploration at level {Level}",
                unmatchedMemberId,
                level + 1);
        }
    }
}
```

### Ключевые изменения

1. **Собираем всех членов source семьи** в список
2. **Убираем из списка** тех, кто был успешно сопоставлен
3. **Добавляем оставшихся (несопоставленных) в очередь** для исследования

### Обработка несопоставленных персон

Когда несопоставленная персона достигается из очереди:

```csharp
var (currentSourceId, level) = queue.Dequeue();

// Проверяем, была ли персона сопоставлена
if (!mappings.ContainsKey(currentSourceId))
{
    _logger.LogDebug(
        "Processing unmatched person {SourceId} at level {Level} for exploration",
        currentSourceId,
        level);

    // Персона НЕ сопоставлена, но мы всё равно обрабатываем её семьи:
    // 1. Ищем семьи, где она супруг/родитель
    // 2. Ищем семьи, где она ребенок
    // 3. Пытаемся сопоставить членов этих семей
    // 4. Добавляем найденных в очередь

    // Для этого нужно изменить логику - сейчас она требует currentDestId:
    // var currentDestId = mappings[currentSourceId].DestinationId;  ← ПРОБЛЕМА

    // Решение: Обрабатывать семьи даже без dest ID
}
```

**Но здесь возникает проблема:** текущая логика требует `currentDestId` для поиска семей в destination.

## Решение: Два режима обработки

### Режим 1: Сопоставленная персона (как сейчас)
- Есть mapping в destination
- Ищем соответствующие семьи в destination
- Сопоставляем членов семей

### Режим 2: Несопоставленная персона (новое)
- НЕТ mapping в destination
- Обрабатываем ТОЛЬКО source семьи
- Для каждого члена source семьи пытаемся найти соответствие во ВСЕХ destination семьях
- Если находим - добавляем в mappings и queue
- Если НЕ находим - добавляем в queue для дальнейшего исследования

### Псевдокод

```csharp
while (queue.Count > 0)
{
    var (currentSourceId, level) = queue.Dequeue();

    if (mappings.ContainsKey(currentSourceId))
    {
        // РЕЖИМ 1: Сопоставленная персона
        ProcessMappedPerson(currentSourceId, level, ...);
    }
    else
    {
        // РЕЖИМ 2: Несопоставленная персона (исследование)
        ProcessUnmappedPerson(currentSourceId, level, ...);
    }
}

void ProcessUnmappedPerson(string sourceId, int level, ...)
{
    var sourcePerson = sourceTree.PersonsById[sourceId];

    _logger.LogInformation(
        "Exploring unmatched person {Name} ({Id}) at level {Level}",
        sourcePerson.ToString(),
        sourceId,
        level);

    // Обрабатываем семьи, где персона — СУПРУГ/РОДИТЕЛЬ
    var sourceFamiliesAsSpouse = TreeNavigator.GetFamiliesAsSpouse(sourceTree, sourceId);

    foreach (var sourceFamily in sourceFamiliesAsSpouse)
    {
        // Для несопоставленной персоны НЕТ dest families
        // Но мы можем попытаться найти семьи через других членов семьи!

        // Если супруг или дети уже сопоставлены, используем их dest families
        var destFamiliesFromMembers = FindDestFamiliesThroughMembers(sourceFamily, mappings, destTree);

        if (destFamiliesFromMembers.Any())
        {
            var (destFamily, familyLog) = _familyMatcher.FindMatchingFamilyWithLog(
                sourceFamily,
                destFamiliesFromMembers,
                mappings,
                sourceTree,
                destTree);

            if (destFamily != null)
            {
                // Теперь попробуем сопоставить ВСЕХ членов, включая текущую персону
                var newMappings = familyMemberMatcher.MatchMembers(...);

                // Добавляем в queue...
            }
        }
    }

    // Аналогично для семей, где персона — РЕБЁНОК
}

IEnumerable<FamilyRecord> FindDestFamiliesThroughMembers(
    FamilyRecord sourceFamily,
    Dictionary<string, PersonMapping> mappings,
    TreeGraph destTree)
{
    var destFamilies = new HashSet<string>();

    // Ищем через супруга
    if (sourceFamily.HusbandId != null && mappings.TryGetValue(sourceFamily.HusbandId, out var husbandMapping))
    {
        var spouseFamilies = TreeNavigator.GetFamiliesAsSpouse(destTree, husbandMapping.DestinationId);
        foreach (var fam in spouseFamilies)
            destFamilies.Add(fam.Id);
    }

    if (sourceFamily.WifeId != null && mappings.TryGetValue(sourceFamily.WifeId, out var wifeMapping))
    {
        var spouseFamilies = TreeNavigator.GetFamiliesAsSpouse(destTree, wifeMapping.DestinationId);
        foreach (var fam in spouseFamilies)
            destFamilies.Add(fam.Id);
    }

    // Ищем через детей
    foreach (var childId in sourceFamily.ChildIds)
    {
        if (mappings.TryGetValue(childId, out var childMapping))
        {
            var childFamilies = TreeNavigator.GetFamiliesAsChild(destTree, childMapping.DestinationId);
            foreach (var fam in childFamilies)
                destFamilies.Add(fam.Id);
        }
    }

    return destTree.Families.Where(f => destFamilies.Contains(f.Id));
}
```

## Альтернативное решение: Продолжать только если кто-то из семьи сопоставлен

Более консервативный подход:

**Добавлять несопоставленных членов семьи в очередь ТОЛЬКО ЕСЛИ**:
1. Семья была найдена (destFamily != null), **И**
2. Хотя бы один член семьи был успешно сопоставлен

**Обоснование:**
- Если семья найдена и кто-то из неё сопоставлен → высокая уверенность в структуре
- Несопоставленные члены могут иметь потомков, которые нужно обработать
- Менее агрессивно, чем добавлять всех подряд

```csharp
if (destFamily != null)
{
    var newMappings = familyMemberMatcher.MatchMembers(context, sourceTree, destTree);

    bool anyMemberMatched = false;
    var allSourceFamilyMembers = new HashSet<string>();
    // ... собираем членов семьи ...

    // Обрабатываем сопоставленных
    foreach (var mapping in newMappings)
    {
        if (!processed.Contains(mapping.SourceId))
        {
            if (validationResult.IsValid)
            {
                mappings[mapping.SourceId] = mapping;
                queue.Enqueue((mapping.SourceId, level + 1));
                processed.Add(mapping.SourceId);
                anyMemberMatched = true;  // ← Запоминаем
                allSourceFamilyMembers.Remove(mapping.SourceId);
            }
        }
    }

    // Добавляем несопоставленных ТОЛЬКО если кто-то был сопоставлен
    if (anyMemberMatched)  // ← УСЛОВИЕ
    {
        foreach (var unmatchedMemberId in allSourceFamilyMembers)
        {
            if (!processed.Contains(unmatchedMemberId))
            {
                queue.Enqueue((unmatchedMemberId, level + 1));
                processed.Add(unmatchedMemberId);
            }
        }
    }
}
```

## Рекомендация

**Начать с альтернативного (консервативного) решения:**

1. ✅ **Проще реализовать** - не требует изменения основного цикла
2. ✅ **Меньше риска** - добавляем только когда есть уверенность (кто-то сопоставлен)
3. ✅ **Решает проблему** - потомки несопоставленных членов будут обработаны
4. ✅ **Легко тестировать**

Затем, если этого недостаточно, можно реализовать полное решение с двумя режимами.

## Тестовый случай

### До изменения
```
Семья F12 найдена:
  Husband: Александр (@I27@) [mapped]
  Wife: Вера (@I34@) [matched, added to queue]
  Child: Юрий (@I35@) [NOT matched, NOT added to queue]

Результат:
  - Юрий и его потомки потеряны
  - ~15-20 человек не обработаны
```

### После изменения
```
Семья F12 найдена:
  Husband: Александр (@I27@) [mapped]
  Wife: Вера (@I34@) [matched, added to queue]
  Child: Юрий (@I35@) [NOT matched, but ADDED to queue for exploration]

Level N: Обработка Юрия (@I35@)
  - Несопоставлен, но обрабатываем его семьи
  - Находим семью F13 (семья Юрия)
  - Пытаемся сопоставить жену и детей Юрия
  - Если успешно - продолжаем BFS через них

Результат:
  - Потомки Юрия обработаны
  - ~15-20 человек найдены
```

## Изменения в коде

### Файл: WaveCompareService.cs

**Метод:** Compare (строки 192-245 и 266-295)

**Изменение:** Добавить логику сбора и добавления несопоставленных членов семьи в очередь

---

**Статус**: 📋 Требует реализации
**Приоритет**: 🔴 Высокий
**Сложность**: ⭐⭐⭐ Средняя
**Влияние**: 🌳 Критическое - влияет на полноту обхода дерева

**Автор**: Claude Sonnet 4.5
**Дата**: 2025-12-14
