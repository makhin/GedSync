# JSON Structure Comparison: compare vs wave-compare

## Command: `compare`

Выводит `CompareResult` напрямую:

```json
{
  "sourceFile": "...",
  "destinationFile": "...",
  "comparedAt": "...",
  "anchors": { ... },
  "options": { ... },
  "statistics": { ... },
  "individuals": {
    "matchedNodes": [ ... ],
    "nodesToUpdate": [
      {
        "sourceId": "@I123@",
        "destinationId": "@I456@",
        "geniProfileId": "profile-xxx",
        "matchScore": 95,
        "matchedBy": "Fuzzy",
        "personSummary": "John Doe (1950-2020)",
        "fieldsToUpdate": [
          {
            "fieldName": "BirthPlace",
            "sourceValue": "New York",
            "destinationValue": "NY",
            "action": "Update"
          }
        ]
      }
    ],
    "nodesToAdd": [
      {
        "sourceId": "@I789@",
        "personData": {
          "firstName": "Jane",
          "lastName": "Doe",
          "birthDate": "1980-01-01",
          ...
        },
        "relatedToNodeId": "@I123@",
        "relationType": "Child",
        "depthFromExisting": 1
      }
    ],
    "nodesToDelete": [ ... ],
    "ambiguousMatches": [ ... ]
  },
  "families": { ... },
  "iterations": [ ... ]
}
```

## Command: `wave-compare`

Выводит составной объект с `summary`, `report` и `waveResult`:

```json
{
  "summary": {
    "source": "...",
    "destination": "...",
    "highConfidenceThreshold": 90
  },
  "report": {
    "sourceFile": "...",
    "destinationFile": "...",
    "anchors": { ... },
    "options": { ... },
    "individuals": {
      "nodesToUpdate": [
        {
          "sourceId": "@I123@",
          "destinationId": "@I456@",
          "geniProfileId": "profile-xxx",
          "matchScore": 95,
          "matchedBy": "Spouse",
          "personSummary": "John Doe (1950-2020)",
          "fieldsToUpdate": [
            {
              "fieldName": "BirthPlace",
              "sourceValue": "New York",
              "destinationValue": "NY",
              "action": "Update"
            }
          ]
        }
      ],
      "nodesToAdd": [
        {
          "sourceId": "@I789@",
          "personData": {
            "firstName": "Jane",
            "lastName": "Doe",
            "birthDate": "1980-01-01",
            ...
          },
          "relatedToNodeId": "@I123@",
          "relationType": "Child",
          "depthFromExisting": 1
        }
      ]
    }
  },
  "waveResult": {
    "sourceFile": "...",
    "destinationFile": "...",
    "comparedAt": "...",
    "anchors": { ... },
    "options": { ... },
    "mappings": [ ... ],
    "unmatchedSource": [ ... ],
    "unmatchedDestination": [ ... ],
    "validationIssues": [ ... ],
    "statisticsByLevel": [ ... ],
    "statistics": { ... }
  }
}
```

## Key Differences

### Structure
- **compare**: Выводит плоский `CompareResult` со всеми данными
- **wave-compare**: Выводит вложенный объект с `report` (high-confidence данные) и `waveResult` (полные результаты волнового алгоритма)

### Content in `individuals`
- **compare**: Содержит `matchedNodes`, `nodesToUpdate`, `nodesToAdd`, `nodesToDelete`, `ambiguousMatches`
- **wave-compare report**: Содержит только `nodesToUpdate` и `nodesToAdd` (только high-confidence, ≥90 score)

### Filtering
- **compare**: Включает все совпадения выше `matchThreshold` (по умолчанию 70)
- **wave-compare**: `report.individuals` включает только high-confidence совпадения (≥90), но `waveResult` содержит все данные

## Common Fields in `nodesToUpdate`

Обе команды используют одинаковую структуру `NodeToUpdate`:
- `sourceId` - ID в source GEDCOM
- `destinationId` - ID в destination GEDCOM
- `geniProfileId` - Geni profile ID (опционально)
- `matchScore` - оценка совпадения (0-100)
- `matchedBy` - как найдено совпадение
- `personSummary` - краткое описание персоны
- `fieldsToUpdate` - список полей для обновления

## Common Fields in `nodesToAdd`

Обе команды используют одинаковую структуру `NodeToAdd`:
- `sourceId` - ID в source GEDCOM
- `personData` - данные о персоне (имя, даты, места и т.д.)
- `relatedToNodeId` - ID связанной персоны (опционально)
- `relationType` - тип связи: Parent, Child, Spouse, Sibling (опционально)
- `depthFromExisting` - глубина от существующих matched узлов

## Compatibility

Обе команды создают совместимые списки:
- ✅ `nodesToUpdate` - список людей для обновления с указанием полей
- ✅ `nodesToAdd` - список людей для добавления с данными и связями

Основное отличие: `wave-compare` фильтрует результаты по high-confidence threshold (≥90) в `report.individuals`, но сохраняет полные данные в `waveResult`.
