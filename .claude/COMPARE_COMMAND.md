# Команда `compare` — Сравнение GEDCOM файлов

## Назначение

Команда `compare` предназначена для сравнения двух GEDCOM файлов:
- **Source** (источник): файл из MyHeritage (GEDCOM 5.5.1)
- **Destination** (назначение): файл из Geni (GEDCOM 5.5.1)

Результат сравнения сохраняется в JSON для последующей обработки и синхронизации с Geni API.

## Функциональные требования

### Категории результатов сравнения

| Категория | Описание |
|-----------|----------|
| **MatchedNodes** | Полностью совпадающие ноды (INDI и FAM) |
| **NodesToUpdate** | Ноды есть в обоих файлах, но в destination отсутствуют данные |
| **NodesToAdd** | Ноды есть в source, отсутствуют в destination |
| **NodesToDelete** | Ноды есть в destination, отсутствуют в source |
| **AmbiguousMatches** | Неоднозначные совпадения (несколько кандидатов) |

### Ключевые требования

1. **Anchor обязателен** — сравнение начинается от якорной персоны
2. **Уникальность matching** — при нескольких кандидатах результат помечается как ambiguous
3. **Сравнение FAM records** — помимо INDI, сравниваются семейные записи
4. **Фото включены** — PhotoUrl входит в список сравниваемых полей
5. **Geni ID из RFN** — идентификатор Geni Profile извлекается из поля RFN, также INDI ID может совпадать

### Глубина добавления новых нод

- `depth=1` (по умолчанию): добавлять только immediate family от существующих
- `depth=2`: immediate family + их родители/дети
- `depth=N`: N уровней от matched/anchor нод

---

## CLI интерфейс

```bash
compare --source <path> --destination <path> --output <json-path>
        --anchor-source <id> --anchor-destination <id>
        [--depth <1>] [--threshold <70>]
        [--include-deletes]
        [--fail-on-ambiguous]
```

### Опции

| Опция | Тип | Обязательный | Default | Описание |
|-------|-----|--------------|---------|----------|
| `--source` | string | Да | — | Путь к GEDCOM из MyHeritage |
| `--destination` | string | Да | — | Путь к GEDCOM из Geni |
| `--output` | string | Да | — | Путь для JSON результата |
| `--anchor-source` | string | Да | — | ID якорной персоны в source (@I1@) |
| `--anchor-destination` | string | Да | — | ID якорной персоны в destination |
| `--depth` | int | Нет | 1 | Глубина новых нод от существующих |
| `--threshold` | int | Нет | 70 | Порог совпадения (0-100) |
| `--include-deletes` | bool | Нет | false | Включить предложения на удаление |
| `--fail-on-ambiguous` | bool | Нет | false | Завершить с ошибкой при ambiguous |

### Примеры использования

```bash
# Базовое сравнение
dotnet run --project GedcomGeniSync.Cli -- compare \
  --source myheritage.ged \
  --destination geni.ged \
  --output comparison.json \
  --anchor-source @I1@ \
  --anchor-destination @I100@

# С увеличенной глубиной и включением удалений
dotnet run --project GedcomGeniSync.Cli -- compare \
  --source myheritage.ged \
  --destination geni.ged \
  --output comparison.json \
  --anchor-source @I1@ \
  --anchor-destination @I100@ \
  --depth 3 \
  --threshold 75 \
  --include-deletes
```

---

## Модели данных

### Корневая структура `CompareResult`

```
CompareResult
├── SourceFile (string)
├── DestinationFile (string)
├── ComparedAt (DateTime)
├── Anchors (AnchorInfo)
├── Options (CompareOptions)
├── Statistics (CompareStatistics)
├── Individuals (IndividualCompareResult)
│   ├── MatchedNodes (List<MatchedNode>)
│   ├── NodesToUpdate (List<NodeToUpdate>)
│   ├── NodesToAdd (List<NodeToAdd>)
│   ├── NodesToDelete (List<NodeToDelete>)
│   └── AmbiguousMatches (List<AmbiguousMatch>)
└── Families (FamilyCompareResult)
    ├── MatchedFamilies (List<MatchedFamily>)
    ├── FamiliesToUpdate (List<FamilyToUpdate>)
    ├── FamiliesToAdd (List<FamilyToAdd>)
    └── FamiliesToDelete (List<FamilyToDelete>)
```

### Модели для Individual (INDI)

#### `MatchedNode`
```csharp
public record MatchedNode(
    string SourceId,              // @I123@
    string DestinationId,         // @I456@
    string? GeniProfileId,        // profile-12345 (из RFN)
    int MatchScore,               // 0-100
    string MatchedBy,             // "RFN" | "INDI_ID" | "Fuzzy"
    string PersonSummary          // "Иванов Иван (1950-2020)"
);
```

#### `NodeToUpdate`
```csharp
public record NodeToUpdate(
    string SourceId,
    string DestinationId,
    string? GeniProfileId,
    int MatchScore,
    string MatchedBy,
    string PersonSummary,
    List<FieldDiff> FieldsToUpdate
);
```

#### `FieldDiff`
```csharp
public record FieldDiff(
    string FieldName,             // "BirthPlace", "PhotoUrl", etc.
    string? SourceValue,
    string? DestinationValue,
    FieldAction Action            // Add | Update | AddPhoto
);

public enum FieldAction { Add, Update, AddPhoto }
```

#### `NodeToAdd`
```csharp
public record NodeToAdd(
    string SourceId,
    PersonData PersonData,        // Полные данные из source
    string? RelatedToNodeId,      // ID существующей ноды
    RelationType RelationType,    // Parent | Child | Spouse | Sibling
    int DepthFromExisting         // Глубина от matched/anchor
);
```

#### `NodeToDelete`
```csharp
public record NodeToDelete(
    string DestinationId,
    string? GeniProfileId,
    string PersonSummary,
    string Reason                 // "Not found in source"
);
```

#### `AmbiguousMatch`
```csharp
public record AmbiguousMatch(
    string SourceId,
    string PersonSummary,
    List<MatchCandidate> Candidates
);

public record MatchCandidate(
    string DestinationId,
    int Score,
    string Summary
);
```

### Модели для Family (FAM)

#### `MatchedFamily`
```csharp
public record MatchedFamily(
    string SourceFamId,           // @F10@
    string DestinationFamId,      // @F20@
    string? HusbandSourceId,
    string? HusbandDestinationId,
    string? WifeSourceId,
    string? WifeDestinationId,
    Dictionary<string, string> ChildrenMapping  // source -> dest
);
```

#### `FamilyToUpdate`
```csharp
public record FamilyToUpdate(
    string SourceFamId,
    string DestinationFamId,
    List<string> MissingChildren, // Дети из source, отсутствующие в dest FAM
    FieldDiff? MarriageDate,
    FieldDiff? MarriagePlace,
    FieldDiff? DivorceDate
);
```

#### `FamilyToAdd`
```csharp
public record FamilyToAdd(
    string SourceFamId,
    string? HusbandId,            // Mapped destination ID или source ID
    string? WifeId,
    List<string> ChildrenIds,
    string? MarriageDate,
    string? MarriagePlace
);
```

### Вспомогательные модели

#### `CompareOptions`
```csharp
public record CompareOptions(
    string AnchorSourceId,        // Обязательный
    string AnchorDestinationId,   // Обязательный
    int NewNodeDepth = 1,
    int MatchThreshold = 70,
    bool IncludeDeleteSuggestions = false,
    bool RequireUniqueMatch = true
);
```

#### `CompareStatistics`
```csharp
public record CompareStatistics(
    IndividualStats Individuals,
    FamilyStats Families
);

public record IndividualStats(
    int TotalSource,
    int TotalDestination,
    int Matched,
    int ToUpdate,
    int ToAdd,
    int ToDelete,
    int Ambiguous
);

public record FamilyStats(
    int TotalSource,
    int TotalDestination,
    int Matched,
    int ToUpdate,
    int ToAdd,
    int ToDelete
);
```

---

## Алгоритм сравнения

### Общий flow

```
1. Загрузка файлов
   ├── GedcomLoader.Load(source) → sourceResult
   └── GedcomLoader.Load(destination) → destResult

2. Валидация anchor
   ├── Проверить anchor в source
   ├── Проверить anchor в destination
   └── Ошибка если не найдены

3. Определение scope
   ├── BFS от anchor в source → sourceScope
   └── BFS от anchor в destination → destScope

4. Matching individuals
   ├── Для каждого person в sourceScope:
   │   ├── Поиск по RFN (точное совпадение) → 100%
   │   ├── Поиск по INDI ID → 100%
   │   └── Fuzzy matching → score
   │
   ├── Классификация:
   │   ├── 0 кандидатов → ToAdd
   │   ├── 1 кандидат, все поля совпадают → Matched
   │   ├── 1 кандидат, есть пустые поля → ToUpdate
   │   └── >1 кандидатов → AmbiguousMatch

5. Фильтрация ToAdd по глубине
   └── BFS от matched нод, включать только depth <= NewNodeDepth

6. Определение ToDelete (если включено)
   └── destScope - matched destinations

7. Matching families
   ├── FAM matched если HUSB и WIFE оба matched
   └── Сравнение детей, дат брака

8. Генерация JSON результата
```

### Приоритет matching

```
1. RFN совпадает         → score=100, matchedBy="RFN"
2. INDI ID совпадает     → score=100, matchedBy="INDI_ID"
3. FuzzyMatcher >= threshold → score=N, matchedBy="Fuzzy"
4. Иначе                 → no match
```

### Логика определения ToUpdate vs Matched

```
Для matched пары (source, dest):
  fieldsToUpdate = []

  для каждого поля в [FirstName, LastName, MaidenName, MiddleName,
                       Nickname, Suffix, BirthDate, DeathDate,
                       BurialDate, BirthPlace, DeathPlace,
                       BurialPlace, Gender, PhotoUrl]:

    if source[field] != null AND dest[field] == null:
      fieldsToUpdate.add(FieldDiff(field, source[field], null, Add))

    # Для дат с разной точностью:
    if source.BirthDate.HasFullDate AND dest.BirthDate.HasOnlyYear:
      fieldsToUpdate.add(FieldDiff("BirthDate", source, dest, Update))

  if fieldsToUpdate.isEmpty():
    → MatchedNode
  else:
    → NodeToUpdate с fieldsToUpdate
```

---

## Формат JSON результата

```json
{
  "sourceFile": "/path/to/myheritage.ged",
  "destinationFile": "/path/to/geni.ged",
  "comparedAt": "2025-01-15T10:30:00Z",
  "anchors": {
    "sourceId": "@I1@",
    "destinationId": "@I100@",
    "geniProfileId": "profile-6000000012345678901",
    "matchConfirmed": true
  },
  "options": {
    "newNodeDepth": 1,
    "matchThreshold": 70,
    "includeDeleteSuggestions": false,
    "requireUniqueMatch": true
  },
  "statistics": {
    "individuals": {
      "totalSource": 150,
      "totalDestination": 120,
      "matched": 95,
      "toUpdate": 15,
      "toAdd": 40,
      "toDelete": 5,
      "ambiguous": 2
    },
    "families": {
      "totalSource": 50,
      "totalDestination": 40,
      "matched": 35,
      "toUpdate": 5,
      "toAdd": 10,
      "toDelete": 0
    }
  },
  "individuals": {
    "matchedNodes": [
      {
        "sourceId": "@I1@",
        "destinationId": "@I100@",
        "geniProfileId": "profile-6000000012345678901",
        "matchScore": 100,
        "matchedBy": "RFN",
        "personSummary": "Иванов Иван Петрович (1950-2020)"
      }
    ],
    "nodesToUpdate": [
      {
        "sourceId": "@I10@",
        "destinationId": "@I110@",
        "geniProfileId": "profile-6000000012345678902",
        "matchScore": 95,
        "matchedBy": "Fuzzy",
        "personSummary": "Иванова Мария Сергеевна (1952-)",
        "fieldsToUpdate": [
          {
            "fieldName": "BirthPlace",
            "sourceValue": "Москва, Россия",
            "destinationValue": null,
            "action": "Add"
          },
          {
            "fieldName": "PhotoUrl",
            "sourceValue": "https://myheritage.com/photo/123.jpg",
            "destinationValue": null,
            "action": "AddPhoto"
          },
          {
            "fieldName": "BirthDate",
            "sourceValue": "15 MAR 1952",
            "destinationValue": "1952",
            "action": "Update"
          }
        ]
      }
    ],
    "nodesToAdd": [
      {
        "sourceId": "@I50@",
        "personData": {
          "firstName": "Пётр",
          "lastName": "Иванов",
          "gender": "Male",
          "birthDate": "1975-05-20",
          "birthPlace": "Санкт-Петербург"
        },
        "relatedToNodeId": "@I1@",
        "relationType": "Child",
        "depthFromExisting": 1
      }
    ],
    "nodesToDelete": [
      {
        "destinationId": "@I200@",
        "geniProfileId": "profile-6000000012345678999",
        "personSummary": "Сидоров Неизвестный (1900-1950)",
        "reason": "Not found in source within comparison scope"
      }
    ],
    "ambiguousMatches": [
      {
        "sourceId": "@I30@",
        "personSummary": "Петров Пётр (1920)",
        "candidates": [
          {
            "destinationId": "@I130@",
            "score": 85,
            "summary": "Петров Пётр Иванович (1920)"
          },
          {
            "destinationId": "@I131@",
            "score": 82,
            "summary": "Петров Пётр Алексеевич (1921)"
          }
        ]
      }
    ]
  },
  "families": {
    "matchedFamilies": [
      {
        "sourceFamId": "@F1@",
        "destinationFamId": "@F10@",
        "husbandSourceId": "@I1@",
        "husbandDestinationId": "@I100@",
        "wifeSourceId": "@I2@",
        "wifeDestinationId": "@I101@",
        "childrenMapping": {
          "@I3@": "@I102@",
          "@I4@": "@I103@"
        }
      }
    ],
    "familiesToUpdate": [
      {
        "sourceFamId": "@F5@",
        "destinationFamId": "@F15@",
        "missingChildren": ["@I50@"],
        "marriageDate": {
          "fieldName": "MarriageDate",
          "sourceValue": "1970-06-15",
          "destinationValue": null,
          "action": "Add"
        },
        "marriagePlace": null,
        "divorceDate": null
      }
    ],
    "familiesToAdd": [
      {
        "sourceFamId": "@F10@",
        "husbandId": "@I50@",
        "wifeId": "@I51@",
        "childrenIds": ["@I52@"],
        "marriageDate": "1995-08-20",
        "marriagePlace": "Москва"
      }
    ],
    "familiesToDelete": []
  }
}
```

---

## Конфигурация

### Секция в `gedsync.yaml`

```yaml
compare:
  newNodeDepth: 1
  matchThreshold: 70
  includeDeleteSuggestions: false
  requireUniqueMatch: true
  fieldsToCompare:
    - FirstName
    - LastName
    - MaidenName
    - MiddleName
    - Nickname
    - Suffix
    - BirthDate
    - DeathDate
    - BurialDate
    - BirthPlace
    - DeathPlace
    - BurialPlace
    - Gender
    - PhotoUrl
```

---

## Сервисы

### Новые сервисы

| Сервис | Файл | Ответственность |
|--------|------|-----------------|
| `IGedcomCompareService` | `GedcomCompareService.cs` | Оркестратор сравнения |
| `IIndividualCompareService` | `IndividualCompareService.cs` | Сравнение INDI записей |
| `IFamilyCompareService` | `FamilyCompareService.cs` | Сравнение FAM записей |
| `IPersonFieldComparer` | `PersonFieldComparer.cs` | Сравнение полей PersonRecord |

### Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| `GedcomLoader` | Парсить RFN tag → `GeniProfileId` |
| `PersonRecord` | Добавить поле `GeniProfileId` |

---

## План имплементации

### Этап 1: Подготовка моделей

| Шаг | Описание | Файл |
|-----|----------|------|
| 1.1 | Обновить `PersonRecord` — добавить `GeniProfileId` | `Models/PersonRecord.cs` |
| 1.2 | Обновить `GedcomLoader` — парсить RFN | `Services/GedcomLoader.cs` |
| 1.3 | Создать модели для Individual compare | `Models/CompareModels.cs` |
| 1.4 | Создать модели для Family compare | `Models/CompareModels.cs` |
| 1.5 | Создать `CompareResult` и `CompareOptions` | `Models/CompareModels.cs` |

### Этап 2: Сервисы сравнения

| Шаг | Описание | Файл |
|-----|----------|------|
| 2.1 | Реализовать `PersonFieldComparer` | `Services/PersonFieldComparer.cs` |
| 2.2 | Реализовать `IndividualCompareService` | `Services/IndividualCompareService.cs` |
| 2.3 | Реализовать `FamilyCompareService` | `Services/FamilyCompareService.cs` |
| 2.4 | Реализовать `GedcomCompareService` | `Services/GedcomCompareService.cs` |

### Этап 3: CLI команда

| Шаг | Описание | Файл |
|-----|----------|------|
| 3.1 | Добавить команду `compare` | `Program.cs` |
| 3.2 | Добавить секцию конфигурации | `Models/Configuration.cs` |
| 3.3 | Интеграция с DI контейнером | `Program.cs` |

### Этап 4: Тестирование

| Шаг | Описание | Файл |
|-----|----------|------|
| 4.1 | Unit-тесты `PersonFieldComparer` | `PersonFieldComparerTests.cs` |
| 4.2 | Unit-тесты `IndividualCompareService` | `IndividualCompareServiceTests.cs` |
| 4.3 | Unit-тесты `FamilyCompareService` | `FamilyCompareServiceTests.cs` |
| 4.4 | Integration-тесты с реальными GEDCOM | `CompareIntegrationTests.cs` |

---

## Тест-кейсы

### Individual matching

| Кейс | Входные данные | Ожидаемый результат |
|------|---------------|---------------------|
| RFN match | source.RFN == dest.RFN | MatchedNode, score=100, matchedBy="RFN" |
| INDI ID match | source.Id == dest.Id | MatchedNode, score=100, matchedBy="INDI_ID" |
| Fuzzy match unique | 1 кандидат >= threshold | MatchedNode или NodeToUpdate |
| Fuzzy match ambiguous | 2+ кандидатов >= threshold | AmbiguousMatch |
| No match | 0 кандидатов | NodeToAdd |
| Partial data | dest.BirthPlace == null | NodeToUpdate с FieldDiff |
| Full date vs year | source="15 MAR 1950", dest="1950" | NodeToUpdate, action=Update |

### Family matching

| Кейс | Входные данные | Ожидаемый результат |
|------|---------------|---------------------|
| Both spouses matched | HUSB и WIFE matched | MatchedFamily |
| Missing children | source FAM имеет больше CHIL | FamilyToUpdate |
| Missing marriage date | dest.MARR DATE == null | FamilyToUpdate |
| No FAM in dest | Супруги matched, FAM нет | FamilyToAdd |

### Depth filtering

| Кейс | Depth | Ожидаемый результат |
|------|-------|---------------------|
| Immediate family | 1 | Только родители, супруги, дети anchor |
| Extended family | 2 | + родители родителей, дети детей |
| Unlimited | -1 или null | Все связанные ноды |

### Edge cases

| Кейс | Описание |
|------|----------|
| Anchor not found | Ошибка с понятным сообщением |
| Empty source | Ошибка или пустой результат |
| Empty destination | Все source → ToAdd |
| Circular references | BFS корректно обрабатывает циклы |
| `--fail-on-ambiguous` | Exit code 1 при ambiguous |

---

## Будущее использование

JSON результат сравнения будет использоваться для:

1. **Ручной review** — пользователь проверяет результат перед синхронизацией
2. **Команда `apply`** (будущая) — применение изменений к Geni через API
3. **Diff visualization** — отображение различий в UI
4. **Отчётность** — статистика по сравнению деревьев

### Workflow

```
GEDCOM (MyHeritage) ──┐
                      ├──► compare ──► JSON ──► review ──► apply ──► Geni API
GEDCOM (Geni export) ─┘
```
