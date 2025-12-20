# План имплементации: Скачивание и сравнение фото из GEDCOM

## Обзор

Добавить функциональность автоматического скачивания фотографий при загрузке GEDCOM файлов с локальным кэшированием и сравнением изображений между сервисами (MyHeritage ↔ Geni).

## Требования

1. **Скачивание при загрузке** - автоматически скачивать фото при `GedcomLoader.Load()`
2. **Кэширование** - не скачивать повторно, если фото уже есть на диске
3. **Сравнение по содержимому** - использовать perceptual hash для определения идентичных фото
4. **Список на обновление** - если фото различаются, добавлять в список действий
5. **Использование кэша** - при upload на Geni брать фото с диска

---

## Архитектура

### Структура хранения

```
{configuredPath}/                    # По умолчанию: ./photos
├── myheritage/                      # Источник определяется по URL
│   ├── @I1@/                        # ID персоны из GEDCOM
│   │   ├── 500668_abc123.jpg        # Оригинальное имя из URL
│   │   └── 500668_def456.jpg
│   └── @I2@/
│       └── photo.jpg
├── geni/                            # Фото из Geni GEDCOM
│   └── profile-6000000012345/
│       └── photo.jpg
├── other/                           # Неизвестные источники
│   └── @I5@/
│       └── unknown_photo.jpg
└── cache.json                       # Индекс кэша
```

### Формат cache.json

```json
{
  "version": 1,
  "entries": {
    "https://sites-cf.mhcache.com/.../photo.jpg": {
      "localPath": "myheritage/@I1@/500668_abc123.jpg",
      "personId": "@I1@",
      "source": "myheritage",
      "fileSize": 171146,
      "contentHash": "sha256:abc123...",
      "perceptualHash": "phash:0x1234567890abcdef",
      "downloadedAt": "2025-01-15T10:30:00Z",
      "lastAccessedAt": "2025-01-15T12:00:00Z"
    }
  }
}
```

### Новые компоненты

```
GedcomGeniSync.Core/
├── Models/
│   ├── PhotoCache/
│   │   ├── PhotoCacheIndex.cs       # Модель cache.json
│   │   ├── PhotoCacheEntry.cs       # Запись о скачанном фото
│   │   └── PhotoCompareResult.cs    # Результат сравнения
│   └── PhotoConfig.cs               # Конфигурация фото (добавить в Configuration.cs)
├── Services/
│   ├── Photo/
│   │   ├── IPhotoCacheService.cs    # Интерфейс кэша
│   │   ├── PhotoCacheService.cs     # Реализация кэша
│   │   ├── IPhotoHashService.cs     # Интерфейс хеширования
│   │   ├── PhotoHashService.cs      # Perceptual hash + SHA256
│   │   ├── IPhotoCompareService.cs  # Интерфейс сравнения
│   │   └── PhotoCompareService.cs   # Логика сравнения фото
│   └── PhotoSourceDetector.cs       # Определение источника по URL
```

---

## Фазы имплементации

### Фаза 1: Модели и конфигурация

**Цель:** Создать базовые модели данных и расширить конфигурацию.

**Файлы:**

1. `GedcomGeniSync.Core/Models/PhotoCache/PhotoCacheEntry.cs`
```csharp
public record PhotoCacheEntry
{
    public required string Url { get; init; }
    public required string LocalPath { get; init; }
    public required string PersonId { get; init; }
    public required string Source { get; init; }  // "myheritage", "geni", "other"
    public long FileSize { get; init; }
    public string? ContentHash { get; init; }      // SHA256
    public string? PerceptualHash { get; init; }   // pHash для сравнения
    public DateTime DownloadedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
}
```

2. `GedcomGeniSync.Core/Models/PhotoCache/PhotoCacheIndex.cs`
```csharp
public class PhotoCacheIndex
{
    public int Version { get; set; } = 1;
    public Dictionary<string, PhotoCacheEntry> Entries { get; set; } = new();
}
```

3. `GedcomGeniSync.Core/Models/PhotoCache/PhotoCompareResult.cs`
```csharp
public record PhotoCompareResult
{
    public required string SourceUrl { get; init; }
    public required string DestinationUrl { get; init; }
    public double Similarity { get; init; }        // 0.0 - 1.0
    public bool IsMatch { get; init; }
    public string? Reason { get; init; }
}
```

4. Добавить в `Configuration.cs`:
```csharp
public class PhotoConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("cacheDirectory")]
    public string CacheDirectory { get; set; } = "./photos";

    [JsonPropertyName("downloadOnLoad")]
    public bool DownloadOnLoad { get; set; } = true;

    [JsonPropertyName("similarityThreshold")]
    public double SimilarityThreshold { get; set; } = 0.95;

    [JsonPropertyName("maxConcurrentDownloads")]
    public int MaxConcurrentDownloads { get; set; } = 4;
}
```

**Критерии завершения:**
- [ ] Все модели компилируются
- [ ] Конфигурация загружается из JSON
- [ ] Unit тесты для моделей

---

### Фаза 2: Сервис кэширования фото

**Цель:** Реализовать скачивание и кэширование фотографий.

**Файлы:**

1. `GedcomGeniSync.Core/Services/Photo/IPhotoCacheService.cs`
```csharp
public interface IPhotoCacheService
{
    /// <summary>
    /// Проверить, есть ли фото в кэше
    /// </summary>
    bool IsCached(string url);

    /// <summary>
    /// Получить запись из кэша
    /// </summary>
    PhotoCacheEntry? GetEntry(string url);

    /// <summary>
    /// Скачать фото и добавить в кэш (если ещё нет)
    /// </summary>
    Task<PhotoCacheEntry?> EnsureDownloadedAsync(string url, string personId);

    /// <summary>
    /// Скачать все фото для персоны
    /// </summary>
    Task<IReadOnlyList<PhotoCacheEntry>> EnsureDownloadedAsync(
        string personId,
        IEnumerable<string> urls);

    /// <summary>
    /// Получить данные фото из кэша
    /// </summary>
    Task<byte[]?> GetPhotoDataAsync(string url);

    /// <summary>
    /// Получить данные фото по локальному пути
    /// </summary>
    Task<byte[]?> GetPhotoDataByPathAsync(string localPath);

    /// <summary>
    /// Сохранить индекс кэша на диск
    /// </summary>
    Task SaveIndexAsync();
}
```

2. `GedcomGeniSync.Core/Services/Photo/PhotoCacheService.cs`
   - Загрузка/сохранение `cache.json`
   - Определение источника по URL (myheritage, geni, other)
   - Скачивание с retry логикой
   - Генерация локального пути
   - Параллельное скачивание с ограничением

3. `GedcomGeniSync.Core/Services/Photo/PhotoSourceDetector.cs`
```csharp
public static class PhotoSourceDetector
{
    public static string DetectSource(string url)
    {
        if (IsMyHeritageUrl(url)) return "myheritage";
        if (IsGeniUrl(url)) return "geni";
        return "other";
    }

    public static bool IsMyHeritageUrl(string url) { ... }
    public static bool IsGeniUrl(string url) { ... }
}
```

**Критерии завершения:**
- [ ] Фото скачиваются и сохраняются на диск
- [ ] cache.json создаётся и обновляется
- [ ] Повторный запрос не скачивает существующее фото
- [ ] Unit тесты с mock HTTP
- [ ] Integration тест с реальным URL

---

### Фаза 3: Сервис хеширования

**Цель:** Вычислять perceptual hash для сравнения изображений.

**Зависимости:**
```xml
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.4" />
```

**Файлы:**

1. `GedcomGeniSync.Core/Services/Photo/IPhotoHashService.cs`
```csharp
public interface IPhotoHashService
{
    /// <summary>
    /// Вычислить SHA256 хеш файла
    /// </summary>
    string ComputeContentHash(byte[] data);

    /// <summary>
    /// Вычислить perceptual hash изображения
    /// </summary>
    ulong ComputePerceptualHash(byte[] imageData);

    /// <summary>
    /// Сравнить два perceptual hash
    /// </summary>
    double CompareHashes(ulong hash1, ulong hash2);
}
```

2. `GedcomGeniSync.Core/Services/Photo/PhotoHashService.cs`
   - SHA256 для точного сравнения
   - Average Hash или pHash для визуального сравнения
   - Hamming distance для оценки схожести

**Алгоритм pHash (упрощённый):**
```
1. Уменьшить изображение до 8x8 пикселей
2. Преобразовать в grayscale
3. Вычислить среднее значение яркости
4. Создать 64-bit хеш: bit=1 если пиксель > среднего
5. Сравнение: Hamming distance / 64 = различие
```

**Критерии завершения:**
- [ ] SHA256 хеш вычисляется корректно
- [ ] pHash вычисляется для различных форматов (jpg, png, webp)
- [ ] Идентичные изображения дают одинаковый хеш
- [ ] Похожие изображения дают близкие хеши
- [ ] Unit тесты с тестовыми изображениями

---

### Фаза 4: Сервис сравнения фото

**Цель:** Сравнивать фото между source и destination персонами.

**Файлы:**

1. `GedcomGeniSync.Core/Services/Photo/IPhotoCompareService.cs`
```csharp
public interface IPhotoCompareService
{
    /// <summary>
    /// Сравнить фото двух персон
    /// </summary>
    Task<PhotoCompareReport> ComparePersonPhotosAsync(
        string sourcePersonId,
        IReadOnlyList<string> sourcePhotoUrls,
        string destPersonId,
        IReadOnlyList<string> destPhotoUrls);
}

public record PhotoCompareReport
{
    /// <summary>
    /// Фото, которые есть в source но нет в destination
    /// </summary>
    public IReadOnlyList<PhotoCacheEntry> NewPhotos { get; init; }

    /// <summary>
    /// Фото, которые совпадают
    /// </summary>
    public IReadOnlyList<PhotoCompareResult> MatchedPhotos { get; init; }

    /// <summary>
    /// Фото, которые похожи но не идентичны (требуют review)
    /// </summary>
    public IReadOnlyList<PhotoCompareResult> SimilarPhotos { get; init; }
}
```

2. `GedcomGeniSync.Core/Services/Photo/PhotoCompareService.cs`
   - Сначала сравнение по SHA256 (быстрое точное совпадение)
   - Затем pHash для оставшихся (визуальное сходство)
   - Формирование отчёта о различиях

**Критерии завершения:**
- [ ] Идентичные фото определяются корректно
- [ ] Новые фото (только в source) выявляются
- [ ] Похожие фото (разное разрешение/сжатие) распознаются
- [ ] Unit тесты для всех сценариев

---

### Фаза 5: Интеграция с GedcomLoader

**Цель:** Автоматически скачивать фото при загрузке GEDCOM.

**Изменения:**

1. `GedcomGeniSync.Core/Services/GedcomLoader.cs`
   - Добавить зависимость от `IPhotoCacheService`
   - После парсинга всех персон - скачать фото
   - Обновить `GedcomLoadResult` с информацией о скачанных фото

2. `GedcomGeniSync.Core/Models/GedcomLoadResult.cs`
```csharp
public record GedcomLoadResult
{
    // ... существующие поля ...

    /// <summary>
    /// Статистика по скачанным фото
    /// </summary>
    public PhotoDownloadStats? PhotoStats { get; init; }
}

public record PhotoDownloadStats
{
    public int TotalUrls { get; init; }
    public int Downloaded { get; init; }
    public int FromCache { get; init; }
    public int Failed { get; init; }
    public TimeSpan Duration { get; init; }
}
```

3. Изменить сигнатуру `GedcomLoader`:
```csharp
public async Task<GedcomLoadResult> LoadAsync(
    string filePath,
    bool downloadPhotos = true,  // новый параметр
    CancellationToken cancellationToken = default)
```

**Критерии завершения:**
- [ ] Фото скачиваются при загрузке GEDCOM
- [ ] Прогресс отображается в консоли
- [ ] Можно отключить через конфигурацию
- [ ] Integration тест с реальным GEDCOM

---

### Фаза 6: Интеграция с PersonFieldComparer

**Цель:** Сравнивать фото по содержимому при сравнении персон.

**Изменения:**

1. `GedcomGeniSync.Core/Services/Compare/PersonFieldComparer.cs`
   - Добавить зависимость от `IPhotoCompareService`
   - Изменить `ComparePhotoUrls()` для использования pHash сравнения
   - Добавить новые типы `FieldAction`: `AddPhoto`, `UpdatePhoto`, `PhotoMatch`

2. Новые поля в `FieldDiff`:
```csharp
public record FieldDiff
{
    // ... существующие поля ...

    /// <summary>
    /// Для фото: similarity score (0.0 - 1.0)
    /// </summary>
    public double? PhotoSimilarity { get; init; }

    /// <summary>
    /// Локальный путь к фото (для upload)
    /// </summary>
    public string? LocalPhotoPath { get; init; }
}
```

**Критерии завершения:**
- [ ] Сравнение использует визуальное сходство
- [ ] Различающиеся фото добавляются в diff с `Action = UpdatePhoto`
- [ ] Локальный путь доступен для последующего upload
- [ ] Unit тесты для photo comparison

---

### Фаза 7: Интеграция с Update/Add командами

**Цель:** Использовать кэшированные фото при upload на Geni.

**Изменения:**

1. `GedcomGeniSync.Cli/Services/AddExecutor.cs`
   - Использовать `IPhotoCacheService.GetPhotoDataAsync()` вместо скачивания
   - Fallback на скачивание если нет в кэше

2. `GedcomGeniSync.Cli/Services/UpdateExecutor.cs`
   - Аналогичные изменения для update операций

3. `GedcomGeniSync.ApiClient/Services/GeniPhotoClient.cs`
   - Добавить перегрузку `AddPhotoFromCacheAsync(string localPath, ...)`

**Критерии завершения:**
- [ ] Upload использует локальные файлы
- [ ] Нет повторного скачивания
- [ ] Fallback работает корректно
- [ ] Integration тест upload из кэша

---

### Фаза 8: Тестирование и документация

**Цель:** Полное покрытие тестами и документация.

**Тесты:**

1. Unit тесты:
   - `PhotoCacheServiceTests.cs`
   - `PhotoHashServiceTests.cs`
   - `PhotoCompareServiceTests.cs`
   - `PhotoSourceDetectorTests.cs`

2. Integration тесты:
   - `PhotoCacheIntegrationTests.cs` - реальное скачивание
   - `PhotoCompareIntegrationTests.cs` - сравнение реальных фото

3. Тестовые данные:
   - `tests/fixtures/photos/` - набор тестовых изображений
   - Идентичные пары
   - Похожие пары (разное сжатие)
   - Разные изображения

**Документация:**

1. Обновить `README.md` - описание функционала
2. Обновить `ARCHITECTURE.md` - новые компоненты
3. Создать `docs/PHOTO_CACHE.md` - детальное описание

**Критерии завершения:**
- [ ] Code coverage > 80% для новых компонентов
- [ ] Все тесты проходят
- [ ] Документация актуальна

---

## Зависимости между фазами

```
Фаза 1 (Модели)
    ↓
Фаза 2 (Кэш) ←── Фаза 3 (Хеширование)
    ↓                    ↓
Фаза 4 (Сравнение) ←─────┘
    ↓
┌───┴───┐
↓       ↓
Фаза 5  Фаза 6
(Loader) (Comparer)
    ↓       ↓
    └───┬───┘
        ↓
    Фаза 7
    (Commands)
        ↓
    Фаза 8
    (Тесты)
```

## Оценка объёма

| Фаза | Новые файлы | Изменённые файлы | Сложность |
|------|-------------|------------------|-----------|
| 1    | 4           | 1                | Низкая    |
| 2    | 3           | 0                | Средняя   |
| 3    | 2           | 0                | Средняя   |
| 4    | 2           | 0                | Средняя   |
| 5    | 0           | 2                | Средняя   |
| 6    | 0           | 2                | Низкая    |
| 7    | 0           | 3                | Низкая    |
| 8    | 5+          | 2                | Низкая    |

**Итого:** ~16 новых файлов, ~10 изменённых файлов

---

## Риски и митигация

| Риск | Вероятность | Влияние | Митигация |
|------|-------------|---------|-----------|
| ImageSharp не поддерживает формат | Низкая | Средняя | Fallback на SHA256 сравнение |
| Большой размер кэша | Средняя | Низкая | Настройка retention policy |
| Медленное скачивание | Средняя | Средняя | Параллельное скачивание, progress bar |
| Изменение URL структуры | Низкая | Средняя | Гибкий PhotoSourceDetector |

---

## Готовность к началу

После утверждения плана начать с **Фазы 1** - создание моделей и конфигурации.
