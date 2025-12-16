# Wave Compare JSON Structure Fix

## Problem

Команда `wave-compare` использовала неопределенный тип `WaveHighConfidenceReport`, что приводило к ошибкам компиляции.

## Solution

### 1. Добавлены недостающие типы в модели

**Файл**: [GedcomGeniSync.Core/Models/Wave/WaveCompareModels.cs](GedcomGeniSync.Core/Models/Wave/WaveCompareModels.cs)

Добавлены два новых record типа:

```csharp
/// <summary>
/// High-confidence report generated from wave compare results.
/// Contains lists of individuals to add and update based on confidence threshold.
/// </summary>
public record WaveHighConfidenceReport
{
    public required string SourceFile { get; init; }
    public required string DestinationFile { get; init; }
    public required AnchorInfo Anchors { get; init; }
    public required WaveCompareOptions Options { get; init; }
    public required WaveIndividualsReport Individuals { get; init; }
}

/// <summary>
/// Individual results from wave compare with high-confidence filtering.
/// Contains lists of nodes to update and add, similar to regular compare command.
/// </summary>
public record WaveIndividualsReport
{
    public required ImmutableList<NodeToUpdate> NodesToUpdate { get; init; }
    public required ImmutableList<NodeToAdd> NodesToAdd { get; init; }
}
```

### 2. Обновлен WaveCompareCommandHandler

**Файл**: [GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs](GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs)

- Удалено локальное определение `WaveIndividualsReport`
- Обновлены ссылки на типы для использования полных имен из `GedcomGeniSync.Core.Models.Wave`
- Метод `BuildWaveReport` теперь возвращает `GedcomGeniSync.Core.Models.Wave.WaveHighConfidenceReport`

## Verification

### JSON Structure

Команда `wave-compare` теперь правильно создает JSON со следующей структурой:

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
          "personSummary": "...",
          "fieldsToUpdate": [ ... ]
        }
      ],
      "nodesToAdd": [
        {
          "sourceId": "@I789@",
          "personData": { ... },
          "relatedToNodeId": "@I123@",
          "relationType": "Child",
          "depthFromExisting": 1
        }
      ]
    }
  },
  "waveResult": { ... }
}
```

### Key Features

✅ **nodesToUpdate** - список людей для обновления с указанием:
- Какие поля отличаются (`fieldsToUpdate`)
- Source и destination ID
- Match score (≥90 для high-confidence)
- Как найдено совпадение (`matchedBy`)

✅ **nodesToAdd** - список людей для добавления с указанием:
- Полных данных о персоне (`personData`)
- Связи с существующей персоной (`relatedToNodeId`, `relationType`)
- Глубины от matched узлов (`depthFromExisting`)

✅ **Совместимость с `compare`** - структуры `NodeToUpdate` и `NodeToAdd` идентичны в обеих командах

## Testing

Добавлен unit test: [GedcomGeniSync.Tests/Wave/WaveHighConfidenceReportTests.cs](GedcomGeniSync.Tests/Wave/WaveHighConfidenceReportTests.cs)

Тесты проверяют:
- Правильность структуры `WaveHighConfidenceReport`
- Наличие всех обязательных полей в `NodeToUpdate`
- Наличие всех обязательных полей в `NodeToAdd`
- Корректную сериализацию в JSON

## Documentation

Создана документация:
- [docs/wave-compare-json-format.md](docs/wave-compare-json-format.md) - подробное описание формата JSON
- [json_structure_comparison.md](json_structure_comparison.md) - сравнение форматов `compare` vs `wave-compare`

## Files Changed

1. ✏️ `GedcomGeniSync.Core/Models/Wave/WaveCompareModels.cs` - добавлены типы
2. ✏️ `GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs` - обновлены ссылки на типы
3. ➕ `GedcomGeniSync.Tests/Wave/WaveHighConfidenceReportTests.cs` - новые тесты
4. ➕ `docs/wave-compare-json-format.md` - документация формата
5. ➕ `json_structure_comparison.md` - сравнение команд

## Usage

```bash
# Запуск команды wave-compare
./GedcomGeniSync.Cli wave-compare \
  --source source.ged \
  --destination dest.ged \
  --anchor-source "@I1@" \
  --anchor-destination "@I1@" \
  --output results.json

# Просмотр списка для обновления
cat results.json | jq '.report.individuals.nodesToUpdate'

# Просмотр списка для добавления
cat results.json | jq '.report.individuals.nodesToAdd'
```
