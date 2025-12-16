# Стратегия реализации команд `update` и `add`

## Обзор

Цель: реализовать две новые CLI команды для синхронизации данных из MyHeritage GEDCOM в Geni.com на основе результатов `wave-compare`.

## Входные данные

1. **JSON файл** - результат команды `wave-compare`, содержащий:
   - `report.individuals.nodesToUpdate` - список профилей для обновления
   - `report.individuals.nodesToAdd` - список новых профилей для добавления

2. **MyHeritage GEDCOM файл** - источник полных данных (включая PhotoUrl)

## Анализ существующей инфраструктуры

### GeniApiClient - имеющиеся методы

**Profile операции (GeniProfileClient):**
- `UpdateProfileAsync(profileId, GeniProfileUpdate)` ✅
- `AddChildAsync(parentProfileId, GeniProfileCreate)` ✅
- `AddParentAsync(childProfileId, GeniProfileCreate)` ✅
- `AddPartnerAsync(profileId, GeniProfileCreate)` ✅
- `AddChildToUnionAsync(unionId, GeniProfileCreate)` ✅
- `AddPartnerToUnionAsync(unionId, GeniProfileCreate)` ✅

**Photo операции (GeniPhotoClient):**
- `AddPhotoFromBytesAsync(profileId, imageData, fileName, caption)` ✅
- `SetMugshotFromBytesAsync(profileId, imageData, fileName)` ✅

**MyHeritage сервис:**
- `MyHeritagePhotoService.DownloadPhotoAsync(url)` ✅

### Geni API - Поддерживаемые поля для Update

Из документации Geni `/profile/update`:

| Поле | Текущая реализация | Статус |
|------|-------------------|--------|
| first_name | ✅ | OK |
| middle_name | ✅ | OK |
| last_name | ✅ | OK |
| maiden_name | ✅ | OK |
| suffix | ✅ | OK |
| gender | ✅ | OK |
| occupation | ✅ | OK |
| about_me | ✅ | OK |
| **names** | ❌ | **Нужно добавить** (мультиязычность) |
| nicknames | ❌ | Нужно добавить |
| title | ❌ | Нужно добавить |
| birth (Event) | ⚠️ Заготовка | Нужно реализовать |
| death (Event) | ⚠️ Заготовка | Нужно реализовать |
| baptism (Event) | ❌ | Нужно добавить |
| burial (Event) | ❌ | Нужно добавить |
| is_alive | ❌ | Нужно добавить |
| cause_of_death | ❌ | Нужно добавить |

**Формат поля `names` (мультиязычные имена):**
```json
{
  "names": {
    "ru": {"first_name": "Иван", "last_name": "Иванов"},
    "en": {"first_name": "Ivan", "last_name": "Ivanov"},
    "he": {"first_name": "איוון", "last_name": "איבנוב"}
  }
}
```
Поддерживаемые поля внутри locale: `first_name`, `middle_name`, `last_name`, `maiden_name`, `suffix`, `title`

**Формат Event объектов:**
```json
{
  "birth": {
    "date": {"day": 15, "month": 3, "year": 1950},
    "location": {"place_name": "Moscow, Russia"}
  }
}
```

### Geni API - Поддерживаемые поля для Add

Для `add-child`, `add-parent`, `add-partner` API поддерживает:
- first_name, middle_name, last_name, maiden_name, suffix, title
- gender, email, is_alive, public
- birth, death, baptism, burial, marriage, divorce (Event objects)
- nicknames, relationship_modifier (adopt/foster)

**Текущая реализация GeniProfileCreate покрывает:** ✅ Достаточно полей

---

## Архитектура команд

### 1. Команда `update`

```
gedsync update --input <wave-compare.json> --gedcom <myheritage.ged> [--token-file <path>] [--dry-run] [--verbose]
```

**Параметры:**
- `--input` (required) - JSON файл от wave-compare
- `--gedcom` (required) - MyHeritage GEDCOM для получения полных данных
- `--token-file` (default: geni_token.json) - файл с Geni токеном
- `--dry-run` - режим симуляции
- `--verbose` - подробный лог
- `--sync-photos` - синхронизировать фото (по умолчанию true)
- `--skip-fields` - пропустить определенные поля (через запятую)

**Алгоритм:**
```
1. Загрузить JSON результат wave-compare
2. Загрузить GEDCOM файл
3. Для каждого NodeToUpdate:
   a. Получить GeniProfileId
   b. Для каждого FieldDiff:
      - Если Action = AddPhoto:
        * Скачать фото через MyHeritagePhotoService
        * Загрузить в Geni через SetMugshotFromBytesAsync
      - Иначе:
        * Собрать GeniProfileUpdate
        * Вызвать UpdateProfileAsync
   c. Логировать результат
4. Вывести статистику
```

### 2. Команда `add`

```
gedsync add --input <wave-compare.json> --gedcom <myheritage.ged> [--token-file <path>] [--dry-run] [--verbose]
```

**Параметры:** аналогично `update`

**Алгоритм:**
```
1. Загрузить JSON результат wave-compare
2. Загрузить GEDCOM файл
3. Создать карту: SourceId -> GeniProfileId для уже существующих
4. Отсортировать NodesToAdd по DepthFromExisting (сначала ближние)
5. Для каждого NodeToAdd:
   a. Найти RelatedToNodeId в карте -> получить GeniProfileId родственника
   b. В зависимости от RelationType:
      - Parent -> AddChildAsync(parent_geni_id, child_data)
      - Child -> AddParentAsync(child_geni_id, parent_data)
      - Spouse -> AddPartnerAsync(profile_geni_id, partner_data)
   c. Сохранить новый GeniProfileId в карту
   d. Если есть PhotoUrl:
      * Скачать и загрузить как mugshot
   e. Логировать результат
6. Вывести статистику
```

---

## Необходимые изменения

### 1. Расширение GeniProfileUpdate (GeniModels.cs)

```csharp
public class GeniProfileUpdate
{
    // Существующие поля...

    // Новые поля:
    [JsonPropertyName("names")]
    public Dictionary<string, Dictionary<string, string>>? Names { get; set; }
    // Пример: {"ru": {"first_name": "Иван"}, "he": {"first_name": "איוון"}}

    [JsonPropertyName("nicknames")]
    public string? Nicknames { get; set; }  // comma-delimited

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; set; }

    [JsonPropertyName("cause_of_death")]
    public string? CauseOfDeath { get; set; }
}
```

### 2. Модель для Event (GeniModels.cs)

```csharp
public class GeniEventInput
{
    [JsonPropertyName("date")]
    public GeniDateInput? Date { get; set; }

    [JsonPropertyName("location")]
    public GeniLocationInput? Location { get; set; }
}

public class GeniDateInput
{
    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

public class GeniLocationInput
{
    [JsonPropertyName("place_name")]
    public string? PlaceName { get; set; }
}
```

### 3. Улучшение CreateFormContent в GeniProfileClient

```csharp
private static FormUrlEncodedContent CreateFormContent(GeniProfileUpdate update)
{
    var values = new Dictionary<string, string>();

    // Существующие поля...

    // Multilingual names
    if (update.Names != null)
    {
        foreach (var (locale, fields) in update.Names)
        {
            foreach (var (field, value) in fields)
            {
                // Формат: names[ru][first_name]=Иван
                values[$"names[{locale}][{field}]"] = value;
            }
        }
    }

    // Birth event
    if (update.Birth is GeniEventInput birth)
    {
        if (birth.Date?.Year != null)
            values["birth[date][year]"] = birth.Date.Year.ToString();
        if (birth.Date?.Month != null)
            values["birth[date][month]"] = birth.Date.Month.ToString();
        if (birth.Date?.Day != null)
            values["birth[date][day]"] = birth.Date.Day.ToString();
        if (!string.IsNullOrEmpty(birth.Location?.PlaceName))
            values["birth[location][place_name]"] = birth.Location.PlaceName;
    }

    // Death event аналогично...

    return new FormUrlEncodedContent(values);
}
```

### 4. Новые файлы команд

```
GedcomGeniSync.Cli/Commands/UpdateCommandHandler.cs
GedcomGeniSync.Cli/Commands/AddCommandHandler.cs
```

### 5. Сервис для выполнения операций

```
GedcomGeniSync.Core/Services/GeniSyncService.cs
```

Этот сервис будет оркестрировать:
- Парсинг JSON
- Маппинг данных
- Вызовы API
- Обработку фото
- Логирование

---

## Структура JSON (wave-compare output)

```json
{
  "report": {
    "individuals": {
      "nodesToUpdate": [
        {
          "sourceId": "@I123@",
          "destinationId": "@I456@",
          "geniProfileId": "profile-12345",
          "matchScore": 95,
          "matchedBy": "Fuzzy",
          "personSummary": "Иванов Иван (1950-2020)",
          "fieldsToUpdate": [
            {
              "fieldName": "BirthPlace",
              "sourceValue": "Москва, Россия",
              "destinationValue": "Москва",
              "action": "Update"
            },
            {
              "fieldName": "PhotoUrl",
              "sourceValue": "https://media.myheritage.com/...",
              "destinationValue": null,
              "action": "AddPhoto"
            }
          ]
        }
      ],
      "nodesToAdd": [
        {
          "sourceId": "@I789@",
          "personData": {
            "firstName": "Петр",
            "lastName": "Иванов",
            "gender": "M",
            "birthDate": "1980",
            "birthPlace": "Москва"
          },
          "relatedToNodeId": "@I123@",
          "relationType": "Child",
          "depthFromExisting": 1
        }
      ]
    }
  }
}
```

---

## Маппинг полей GEDCOM -> Geni API

| FieldName (JSON) | Geni API параметр | Примечание |
|------------------|-------------------|------------|
| FirstName | first_name | |
| MiddleName | middle_name | |
| LastName | last_name | |
| MaidenName | maiden_name | |
| Suffix | suffix | |
| Nickname | nicknames | comma-delimited |
| Gender | gender | "male" / "female" |
| BirthDate | birth[date][...] | Парсить дату |
| BirthPlace | birth[location][place_name] | |
| DeathDate | death[date][...] | Парсить дату |
| DeathPlace | death[location][place_name] | |
| BurialDate | burial[date][...] | |
| BurialPlace | burial[location][place_name] | |
| Occupation | occupation | |
| PhotoUrl | (отдельный API) | add-mugshot |

---

## Обработка фотографий

### Workflow для PhotoUrl:

```
1. Проверить IsMyHeritageUrl(sourceValue)
2. Скачать: MyHeritagePhotoService.DownloadPhotoAsync(url)
3. Загрузить в Geni: GeniPhotoClient.SetMugshotFromBytesAsync(
     profileId,
     downloadResult.Data,
     downloadResult.FileName
   )
```

### Обработка ошибок:
- Если скачивание не удалось - логировать warning, продолжить
- Если загрузка не удалась - логировать error, продолжить
- Не прерывать общий процесс из-за ошибок с фото

---

## Обработка ошибок и retry

```csharp
public class UpdateResult
{
    public int TotalProcessed { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public int PhotosUploaded { get; set; }
    public int PhotosFailed { get; set; }
    public List<UpdateError> Errors { get; set; }
}

public class UpdateError
{
    public string SourceId { get; set; }
    public string GeniProfileId { get; set; }
    public string FieldName { get; set; }
    public string ErrorMessage { get; set; }
}
```

---

## Порядок реализации

### Фаза 1: Подготовка (1-2 часа)
1. Расширить GeniProfileUpdate модель
2. Добавить GeniEventInput, GeniDateInput, GeniLocationInput
3. Обновить CreateFormContent для поддержки Event объектов

### Фаза 2: Update команда (2-3 часа)
1. Создать UpdateCommandHandler.cs
2. Реализовать парсинг JSON
3. Реализовать маппинг FieldDiff -> GeniProfileUpdate
4. Интегрировать с фото-сервисами
5. Добавить статистику и репорт

### Фаза 3: Add команда (2-3 часа)
1. Создать AddCommandHandler.cs
2. Реализовать алгоритм сортировки по глубине
3. Реализовать выбор правильного API (add-child/add-parent/add-partner)
4. Отслеживание созданных профилей
5. Интегрировать с фото-сервисами

### Фаза 4: Тестирование (1-2 часа)
1. Unit тесты для маппинга
2. Integration тесты с dry-run
3. Документация

---

## Пример использования

```bash
# 1. Сначала сравнить деревья
gedsync wave-compare \
  --source myheritage.ged \
  --destination geni-export.ged \
  --anchor-source @I1@ \
  --anchor-destination @I100@ \
  --output comparison.json

# 2. Обновить существующие профили
gedsync update \
  --input comparison.json \
  --gedcom myheritage.ged \
  --dry-run

# 3. Если всё ок - выполнить
gedsync update \
  --input comparison.json \
  --gedcom myheritage.ged

# 4. Добавить новые профили
gedsync add \
  --input comparison.json \
  --gedcom myheritage.ged \
  --dry-run

# 5. Если всё ок - выполнить
gedsync add \
  --input comparison.json \
  --gedcom myheritage.ged
```

---

## Риски и ограничения

1. **Rate limiting Geni API** - уже реализован ThrottleAsync, но возможно потребуется увеличить задержки
2. **Большие батчи** - для сотен профилей нужен progress reporting
3. **Циклические зависимости при add** - если A -> B -> C, нужно правильно сортировать
4. **Фото MyHeritage** - могут быть защищены, требуется авторизация
5. **Частичные ошибки** - нужно продолжать работу даже при ошибках отдельных профилей

---

## Вопросы для уточнения

1. Нужен ли отдельный режим для только фото (`--photos-only`)?
2. Нужна ли поддержка resume (state file)?
3. Какой приоритет у разных типов полей при update?
4. Нужно ли подтверждение перед каждым update в interactive режиме?
