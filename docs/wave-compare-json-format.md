# Wave Compare JSON Output Format

## Overview

Команда `wave-compare` создает JSON файл с результатами сравнения двух GEDCOM файлов по волновому алгоритму. Результат содержит как полные данные волнового сравнения, так и high-confidence отчет со списками для добавления и обновления.

## Root Structure

```json
{
  "summary": {
    "source": "путь к исходному файлу",
    "destination": "путь к целевому файлу",
    "highConfidenceThreshold": 90
  },
  "report": { ... },
  "waveResult": { ... }
}
```

## Summary Section

Краткая информация о сравнении:

```json
{
  "source": "/path/to/source.ged",
  "destination": "/path/to/dest.ged",
  "highConfidenceThreshold": 90
}
```

- `source` - путь к исходному GEDCOM файлу
- `destination` - путь к целевому GEDCOM файлу
- `highConfidenceThreshold` - порог уверенности для включения в отчет (по умолчанию 90)

## Report Section

High-confidence отчет с отфильтрованными результатами (только совпадения ≥90):

```json
{
  "sourceFile": "/path/to/source.ged",
  "destinationFile": "/path/to/dest.ged",
  "anchors": {
    "sourceId": "@I1@",
    "destinationId": "@I2@",
    "sourcePersonSummary": "Иван Иванов (1950-2020)",
    "destinationPersonSummary": "Иван Иванов (1950-2020)"
  },
  "options": {
    "maxLevel": 10,
    "thresholdStrategy": "Adaptive",
    "baseThreshold": 60
  },
  "individuals": {
    "nodesToUpdate": [ ... ],
    "nodesToAdd": [ ... ]
  }
}
```

### Report Fields

- `sourceFile` - исходный файл
- `destinationFile` - целевой файл
- `anchors` - информация о якорных персонах
- `options` - параметры волнового сравнения
- `individuals` - результаты сравнения людей

## NodesToUpdate Array

Список людей, которые существуют в обоих деревьях и требуют обновления:

```json
{
  "sourceId": "@I123@",
  "destinationId": "@I456@",
  "geniProfileId": "profile-12345",
  "matchScore": 95,
  "matchedBy": "Spouse",
  "personSummary": "Иван Петров (1960-)",
  "fieldsToUpdate": [
    {
      "fieldName": "BirthPlace",
      "sourceValue": "Москва",
      "destinationValue": "Moscow",
      "action": "Update"
    },
    {
      "fieldName": "DeathDate",
      "sourceValue": "2020-05-15",
      "destinationValue": null,
      "action": "Add"
    }
  ]
}
```

### NodeToUpdate Fields

- `sourceId` - ID в исходном GEDCOM (@I123@)
- `destinationId` - ID в целевом GEDCOM (@I456@)
- `geniProfileId` - ID профиля Geni (опционально)
- `matchScore` - оценка совпадения 0-100 (≥90 для high-confidence)
- `matchedBy` - как найдено: "Anchor", "Spouse", "Parent", "Child", "Sibling"
- `personSummary` - краткое описание персоны
- `fieldsToUpdate` - список полей для обновления

### FieldDiff Structure

- `fieldName` - название поля (BirthPlace, DeathDate, FirstName и т.д.)
- `sourceValue` - значение в исходном файле
- `destinationValue` - значение в целевом файле (может быть null)
- `action` - действие: "Add", "Update", "AddPhoto"

## NodesToAdd Array

Список людей из исходного дерева, которых нужно добавить в целевое:

```json
{
  "sourceId": "@I789@",
  "personData": {
    "firstName": "Мария",
    "lastName": "Петрова",
    "maidenName": "Иванова",
    "gender": "F",
    "birthDate": "1985-03-20",
    "birthPlace": "Санкт-Петербург",
    "deathDate": null,
    "deathPlace": null
  },
  "relatedToNodeId": "@I123@",
  "relationType": "Child",
  "depthFromExisting": 1
}
```

### NodeToAdd Fields

- `sourceId` - ID в исходном GEDCOM
- `personData` - полные данные о персоне
- `relatedToNodeId` - ID связанной персоны (которая уже существует в целевом дереве)
- `relationType` - тип связи: "Parent", "Child", "Spouse", "Sibling"
- `depthFromExisting` - глубина от существующих matched узлов

### PersonData Fields

- `firstName` - имя
- `lastName` - фамилия
- `maidenName` - девичья фамилия (опционально)
- `middleName` - отчество (опционально)
- `suffix` - суффикс (Jr., Sr. и т.д., опционально)
- `nickname` - прозвище (опционально)
- `gender` - пол: "M", "F"
- `birthDate` - дата рождения
- `birthPlace` - место рождения
- `deathDate` - дата смерти (опционально)
- `deathPlace` - место смерти (опционально)
- `burialDate` - дата захоронения (опционально)
- `burialPlace` - место захоронения (опционально)
- `photoUrl` - URL фотографии (опционально)

## WaveResult Section

Полные результаты волнового алгоритма (включает все совпадения, не только high-confidence):

```json
{
  "sourceFile": "/path/to/source.ged",
  "destinationFile": "/path/to/dest.ged",
  "comparedAt": "2025-12-16T12:00:00Z",
  "anchors": { ... },
  "options": { ... },
  "mappings": [ ... ],
  "unmatchedSource": [ ... ],
  "unmatchedDestination": [ ... ],
  "validationIssues": [ ... ],
  "statisticsByLevel": [ ... ],
  "statistics": { ... }
}
```

### Key Sections in WaveResult

- `mappings` - все найденные сопоставления персон
- `unmatchedSource` - персоны из source без совпадений
- `unmatchedDestination` - персоны из destination без совпадений
- `validationIssues` - проблемы валидации
- `statisticsByLevel` - статистика по уровням BFS
- `statistics` - общая статистика

## Usage Example

```bash
# Запуск команды
./GedcomGeniSync.Cli wave-compare \
  --source source.ged \
  --destination dest.ged \
  --anchor-source "@I1@" \
  --anchor-destination "@I1@" \
  --max-level 10 \
  --threshold-strategy adaptive \
  --base-threshold 60 \
  --output results.json

# Извлечение списка для обновления
cat results.json | jq '.report.individuals.nodesToUpdate'

# Извлечение списка для добавления
cat results.json | jq '.report.individuals.nodesToAdd'

# Подсчет количества обновлений
cat results.json | jq '.report.individuals.nodesToUpdate | length'

# Подсчет количества добавлений
cat results.json | jq '.report.individuals.nodesToAdd | length'
```

## Comparison with `compare` Command

### Similarities

Обе команды (`compare` и `wave-compare`) используют одинаковые структуры для:
- `NodeToUpdate` - люди для обновления
- `NodeToAdd` - люди для добавления
- `FieldDiff` - различия в полях
- `PersonData` - данные о персоне

### Differences

| Aspect | compare | wave-compare |
|--------|---------|-------------|
| Algorithm | Iterative depth-first | BFS wave propagation |
| Output structure | Flat `CompareResult` | Nested with `report` + `waveResult` |
| Filtering | Single threshold (default 70) | Dual: base threshold + high-confidence (90) |
| Additional data | Matched nodes, deletes, ambiguous | Full wave mappings, level statistics |

### Migration Between Commands

Если вы обрабатываете результаты команды `compare`, для перехода на `wave-compare`:
1. Используйте `report.individuals` вместо `individuals`
2. Доступ к полным данным через `waveResult` (если нужно)
3. Структура `nodesToUpdate` и `nodesToAdd` идентична

## Implementation Details

Команда фильтрует результаты волнового алгоритма:

1. **NodesToUpdate**: включает только совпадения с `matchScore >= 90` и имеющие различия в полях
2. **NodesToAdd**: включает только несовпавшие персоны, имеющие связь с high-confidence совпадением (≥90)

Алгоритм в [WaveCompareCommandHandler.cs:211-282](../GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs#L211-L282):

```csharp
// Фильтрация для nodesToUpdate
foreach (var mapping in mappings.Where(m => m.MatchScore >= 90))
{
    var differences = fieldComparer.CompareFields(sourcePerson, destPerson);
    if (differences.Any())
    {
        nodesToUpdate.Add(...);
    }
}

// Фильтрация для nodesToAdd
foreach (var unmatched in result.UnmatchedSource)
{
    var relation = FindHighConfidenceRelation(person, mappings, threshold: 90);
    if (relation != null)
    {
        nodesToAdd.Add(...);
    }
}
```
