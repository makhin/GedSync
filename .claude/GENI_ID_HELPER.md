# GeniIdHelper - Работа с Geni Profile IDs

## Назначение

`GeniIdHelper` - вспомогательный класс для работы с идентификаторами Geni Profile в различных форматах.

## Проблема

Geni Profile ID может быть представлен в разных форматах в зависимости от источника:

| Формат | Пример | Источник |
|--------|--------|----------|
| GEDCOM INDI | `@I6000000206529622827@` | GEDCOM файл, экспортированный из Geni |
| Geni RFN | `geni:6000000206529622827` | RFN тег в GEDCOM файле из Geni |
| Geni Profile | `profile-6000000206529622827` | Geni API |
| Numeric | `6000000206529622827` | Чистый ID |

Все эти форматы содержат **один и тот же численный ID** (`6000000206529622827`), который уникально идентифицирует профиль в Geni.

## Ключевое наблюдение

Когда Geni экспортирует GEDCOM файл:
- **GEDCOM INDI ID** = `@I` + numeric_id + `@`
- **RFN тег** = `geni:` + numeric_id

Пример из реального файла:
```gedcom
0 @I6000000206529622827@ INDI
1 NAME Александр Владимирович /Махин/
...
1 RFN geni:6000000206529622827
```

Это означает, что можно сопоставлять записи по численному ID!

## Использование

### 1. Извлечение численного ID

```csharp
using GedcomGeniSync.Utils;

// Из любого формата
var numericId = GeniIdHelper.ExtractNumericId("@I6000000206529622827@");
// Результат: "6000000206529622827"

var numericId2 = GeniIdHelper.ExtractNumericId("geni:6000000206529622827");
// Результат: "6000000206529622827"
```

### 2. Сравнение двух ID

```csharp
// Проверить, что два ID относятся к одному профилю
bool isSame = GeniIdHelper.IsSameGeniProfile(
    "@I6000000206529622827@",      // GEDCOM ID из source
    "geni:6000000206529622827"     // RFN из destination
);
// Результат: true
```

### 3. Конвертация форматов

```csharp
// В GEDCOM INDI формат
var indiId = GeniIdHelper.ToGedcomIndiId("6000000206529622827");
// Результат: "@I6000000206529622827@"

// В Geni RFN формат
var rfnId = GeniIdHelper.ToGeniRfnFormat("@I6000000206529622827@");
// Результат: "geni:6000000206529622827"

// В Geni Profile формат
var profileId = GeniIdHelper.ToGeniProfileFormat("geni:6000000206529622827");
// Результат: "profile-6000000206529622827"
```

### 4. Использование с PersonRecord

```csharp
var person = new PersonRecord
{
    Id = "@I6000000206529622827@",
    GeniProfileId = "geni:6000000206529622827",
    // ... other fields
};

// Получить численный ID
var numericId = person.GetNumericGeniId();
// Результат: "6000000206529622827"
```

## Применение в Compare алгоритме

При сравнении двух GEDCOM файлов (source и destination) можно использовать следующие стратегии matching:

### Стратегия 1: Точное совпадение по RFN
```csharp
// Если оба файла экспортированы из Geni и имеют RFN теги
if (sourceRecord.GeniProfileId != null &&
    destRecord.GeniProfileId != null)
{
    if (GeniIdHelper.IsSameGeniProfile(
        sourceRecord.GeniProfileId,
        destRecord.GeniProfileId))
    {
        // Matched by RFN - score 100%, matchedBy: "RFN"
    }
}
```

### Стратегия 2: Совпадение по INDI ID
```csharp
// Если оба файла экспортированы из Geni
// INDI ID содержит тот же численный ID, что и RFN
if (GeniIdHelper.IsSameGeniProfile(
    sourceRecord.Id,
    destRecord.Id))
{
    // Matched by INDI_ID - score 100%, matchedBy: "INDI_ID"
}
```

### Стратегия 3: Кросс-проверка
```csharp
// Source INDI ID vs Destination RFN
if (destRecord.GeniProfileId != null &&
    GeniIdHelper.IsSameGeniProfile(
        sourceRecord.Id,
        destRecord.GeniProfileId))
{
    // Matched - score 100%
}
```

## Тесты

Класс покрыт 45 unit-тестами, проверяющими:
- ✅ Извлечение ID из всех поддерживаемых форматов
- ✅ Обработку невалидных входных данных
- ✅ Сравнение ID в разных форматах
- ✅ Конвертацию между форматами
- ✅ Реальные сценарии из Geni-экспортированных файлов

См. [GeniIdHelperTests.cs](../GedcomGeniSync.Tests/GeniIdHelperTests.cs)

## Файлы

- **Класс**: [GeniIdHelper.cs](../GedcomGeniSync.Core/Utils/GeniIdHelper.cs)
- **Тесты**: [GeniIdHelperTests.cs](../GedcomGeniSync.Tests/GeniIdHelperTests.cs)
- **PersonRecord extension**: [PersonRecord.cs:150](../GedcomGeniSync.Core/Models/PersonRecord.cs#L150)
