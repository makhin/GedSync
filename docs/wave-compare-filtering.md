# Wave Compare Filtering Logic

## Overview

Команда `wave-compare` применяет двухуровневую фильтрацию результатов:

1. **Base Threshold** (по умолчанию 60) - используется волновым алгоритмом для поиска совпадений
2. **High-Confidence Threshold** (фиксированный 90) - используется для отчета `report.individuals`

## Data Flow

```
Source GEDCOM + Destination GEDCOM
          ↓
    Wave Algorithm
    (threshold: 60)
          ↓
   All Mappings (0-100 score)
          ↓
    ┌─────────┴─────────┐
    ↓                   ↓
High-Confidence      Low-Confidence
(score ≥ 90)        (score < 90)
    ↓                   ↓
report.individuals   waveResult.mappings
(filtered)           (all data)
```

## Filtering Rules

### NodesToUpdate

Включаются в `report.individuals.nodesToUpdate` если:

```
✓ MatchScore >= 90
AND
✓ FieldDifferences.Any()
```

**Пример**:
```json
{
  "sourceId": "@I1@",
  "destinationId": "@I2@",
  "matchScore": 95,  // ≥ 90 ✓
  "fieldsToUpdate": [
    {
      "fieldName": "BirthPlace",
      "sourceValue": "Moscow",
      "destinationValue": "Москва"
    }
  ]  // Has differences ✓
}
```

**Исключается если**:
- MatchScore < 90 (даже если есть различия)
- MatchScore >= 90, но нет различий в полях (perfect match)

### NodesToAdd

Включаются в `report.individuals.nodesToAdd` если:

```
✓ Person is unmatched in source
AND
✓ Person has relation to high-confidence match
  (spouse/parent/child/sibling with score ≥ 90)
```

**Алгоритм поиска связи** (приоритет сверху вниз):

```csharp
1. Check spouses    → if any spouse has matchScore ≥ 90
2. Check father     → if father has matchScore ≥ 90
3. Check mother     → if mother has matchScore ≥ 90
4. Check children   → if any child has matchScore ≥ 90
5. Check siblings   → if any sibling has matchScore ≥ 90
```

**Пример включения**:

```json
// Source tree:
// @I1@ (matched, score=95) ←─┐
//                             ├─ spouse
// @I2@ (unmatched)          ←─┘

{
  "sourceId": "@I2@",
  "relatedToNodeId": "@I1@",  // spouse with score ≥ 90 ✓
  "relationType": "Spouse",
  "personData": { ... }
}
```

**Пример исключения**:

```json
// Source tree:
// @I1@ (matched, score=65) ←─┐
//                            ├─ spouse
// @I2@ (unmatched)         ←─┘

// @I2@ excluded from nodesToAdd
// because @I1@ matchScore=65 < 90 ✗
```

## Statistics

### Report vs WaveResult

| Metric | report | waveResult |
|--------|--------|-----------|
| nodesToUpdate | Only ≥90 with diffs | N/A |
| nodesToAdd | Only related to ≥90 | N/A |
| mappings | N/A | All (≥60) |
| unmatchedSource | N/A | All unmatched |
| unmatchedDestination | N/A | All unmatched |

### Example Numbers

Предположим результаты волнового сравнения:

```
Total source persons: 1000
Total mappings found: 800 (threshold ≥60)
  - High confidence (≥90): 600
  - Medium confidence (60-89): 200
Unmatched source: 200
```

Тогда в отчете:

```
report.individuals.nodesToUpdate:
  ≤ 600 (только те из 600 high-confidence, у которых есть различия)

report.individuals.nodesToAdd:
  ≤ 200 (только те из 200 unmatched, у которых есть связь с ≥90)

waveResult.mappings:
  = 800 (все найденные)

waveResult.unmatchedSource:
  = 200 (все несовпавшие)
```

## Code Reference

Логика фильтрации реализована в:
- [WaveCompareCommandHandler.cs:211-282](../GedcomGeniSync.Cli/Commands/WaveCompareCommandHandler.cs#L211-L282)

### NodesToUpdate Filtering

```csharp
var updates = ImmutableList.CreateBuilder<NodeToUpdate>();
foreach (var mapping in mappingBySource.Values
    .Where(m => m.MatchScore >= confidenceThreshold))  // ≥90
{
    var differences = fieldComparer.CompareFields(sourcePerson, destPerson);
    if (differences.Any())  // Only if has differences
    {
        updates.Add(new NodeToUpdate { ... });
    }
}
```

### NodesToAdd Filtering

```csharp
var additions = ImmutableList.CreateBuilder<NodeToAdd>();
foreach (var unmatched in result.UnmatchedSource)
{
    var relation = FindHighConfidenceRelation(
        sourcePerson,
        mappingBySource,
        confidenceThreshold: 90);  // Check relations with ≥90

    if (relation != null)  // Only if has high-confidence relation
    {
        additions.Add(new NodeToAdd { ... });
    }
}
```

### High-Confidence Relation Search

```csharp
private static (string RelatedSourceId, CompareRelationType RelationType)?
    FindHighConfidenceRelation(person, mappings, threshold)
{
    // Priority order:
    // 1. Spouse
    if (person.SpouseIds.Any(id => HasHighConfidence(id)))
        return (spouseId, CompareRelationType.Spouse);

    // 2. Father
    if (HasHighConfidence(person.FatherId))
        return (person.FatherId, CompareRelationType.Child);

    // 3. Mother
    if (HasHighConfidence(person.MotherId))
        return (person.MotherId, CompareRelationType.Child);

    // 4. Children
    var child = person.ChildrenIds.FirstOrDefault(HasHighConfidence);
    if (child != null)
        return (child, CompareRelationType.Parent);

    // 5. Siblings
    var sibling = person.SiblingIds.FirstOrDefault(HasHighConfidence);
    if (sibling != null)
        return (sibling, CompareRelationType.Sibling);

    return null;  // No high-confidence relation found
}
```

## Visual Example

```
Source Tree:
    @I1@ (John, MatchScore=95)  ←─────┐
      ├── spouse: @I2@ (Mary, MatchScore=92)
      ├── child:  @I3@ (Bob, MatchScore=88)   ← Medium confidence
      └── child:  @I4@ (Alice, UNMATCHED)

Destination Tree:
    @D1@ (John)
      ├── spouse: @D2@ (Mary)
      └── child:  @D3@ (Bob)

Results:

report.individuals.nodesToUpdate = [
  {
    sourceId: "@I1@",
    destinationId: "@D1@",
    matchScore: 95,          // ≥90 ✓
    fieldsToUpdate: [...]    // Has diffs ✓
  },
  {
    sourceId: "@I2@",
    destinationId: "@D2@",
    matchScore: 92,          // ≥90 ✓
    fieldsToUpdate: [...]    // Has diffs ✓
  }
  // @I3@ excluded: score=88 < 90
]

report.individuals.nodesToAdd = [
  {
    sourceId: "@I4@",
    personData: { firstName: "Alice", ... },
    relatedToNodeId: "@I1@",  // parent with score=95 ≥90 ✓
    relationType: "Child"
  }
]

waveResult.mappings = [
  { sourceId: "@I1@", destinationId: "@D1@", matchScore: 95 },
  { sourceId: "@I2@", destinationId: "@D2@", matchScore: 92 },
  { sourceId: "@I3@", destinationId: "@D3@", matchScore: 88 }
]
```

## Recommendations

### For Updates
- Проверяйте `report.individuals.nodesToUpdate` для автоматических обновлений
- Используйте `waveResult.mappings` для ревью всех совпадений включая medium-confidence

### For Additions
- `report.individuals.nodesToAdd` содержит только "безопасные" добавления (связаны с high-confidence)
- Для агрессивного добавления используйте `waveResult.unmatchedSource` с ручной проверкой

### Threshold Tuning
- `--base-threshold 60` - для волнового поиска (можно понизить для большего охвата)
- High-confidence threshold (90) - фиксированный, для безопасных автоматических операций
