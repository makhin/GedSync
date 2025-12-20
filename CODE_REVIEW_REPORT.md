# GedSync Code Review Report

## Обзор

Проведена полная ревизия кодовой базы GedSync. Выявлены проблемы по следующим категориям:

| Категория | Критические | Высокие | Средние | Низкие |
|-----------|------------|---------|---------|--------|
| Баги | 1 | 2 | 4 | 2 |
| Производительность | 2 | 6 | 5 | 2 |
| Дублирование кода | - | 4 | 4 | 3 |
| Упрощение | - | 3 | 5 | 4 |

---

## 1. КРИТИЧЕСКИЕ ПРОБЛЕМЫ (Исправить немедленно)

### 1.1 Блокирующие вызовы `.Result` на async методах

**Файлы:**
- `GedcomGeniSync.Cli/Commands/AddCommandHandler.cs:69,84`
- `GedcomGeniSync.Cli/Commands/UpdateCommandHandler.cs:87,102`
- `GedcomGeniSync.Cli/Commands/ProfileCommandHandler.cs:50,75`

**Проблема:**
```csharp
var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile).Result;
```

**Почему критично:**
- Вызывает deadlock в определённых контекстах синхронизации
- Блокирует thread pool
- Анти-паттерн для async кода

**Решение:**
```csharp
// Вариант 1: Использовать GetAwaiter().GetResult() (минимальное изменение)
var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile).GetAwaiter().GetResult();

// Вариант 2: Вынести загрузку токена в async factory (рекомендуется)
// Создать TokenProvider сервис
```

---

### 1.2 Regex без компиляции на горячих путях

**Файлы:**
- `GedcomGeniSync.ApiClient/Utils/GeniIdHelper.cs:26-41`
- `GedcomGeniSync.Core/Services/GedcomLoader.cs:1029,1044,1087,1088,1102,1180`
- `GedcomGeniSync.Cli/Services/UpdateExecutor.cs:406,411,435`

**Проблема:**
```csharp
var indiMatch = Regex.Match(id, @"@I(\d+)@");  // Создаёт новый Regex каждый вызов
```

**Влияние:** При загрузке GEDCOM с 50,000+ персон вызывается миллионы раз.

**Решение:**
```csharp
private static readonly Regex IndiPattern = new(@"@I(\d+)@", RegexOptions.Compiled);
private static readonly Regex GeniPattern = new(@"geni:(\d+)", RegexOptions.Compiled);
private static readonly Regex ProfilePattern = new(@"profile-(\d+)", RegexOptions.Compiled);
private static readonly Regex NumericPattern = new(@"^\d+$", RegexOptions.Compiled);

public static string? ExtractNumericId(string id)
{
    var indiMatch = IndiPattern.Match(id);
    if (indiMatch.Success)
        return indiMatch.Groups[1].Value;
    // ...
}
```

---

## 2. ВЫСОКОПРИОРИТЕТНЫЕ ПРОБЛЕМЫ

### 2.1 Словари создаются при каждом вызове метода

**Файлы:**
- `GedcomGeniSync.Core/Models/PersonRecord.cs:441-467`
- `GedcomGeniSync.Cli/Services/UpdateExecutor.cs:418-423`

**Проблема:**
```csharp
private static (int? year, int? month, int? day) ParseDatePart(string dateStr)
{
    // Создаётся каждый вызов! 25+ записей.
    var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ...
    };
}
```

**Решение:**
```csharp
private static readonly Dictionary<string, int> MonthsMap = new(StringComparer.OrdinalIgnoreCase)
{
    ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4,
    ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AUG"] = 8,
    ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DEC"] = 12,
    // Russian
    ["ЯНВ"] = 1, ["ФЕВ"] = 2, ...
};
```

### 2.2 Цепочки String.Replace создают много аллокаций

**Файлы:**
- `GedcomGeniSync.Core/Utils/NameNormalizer.cs:25-28`
- `GedcomGeniSync.Core/Services/FuzzyMatcherService.cs:560-562,803-805`
- `GedcomGeniSync.Core/Models/PersonRecord.cs:360-385`

**Проблема:**
```csharp
return transliterated
    .ToLowerInvariant()
    .Replace("-", "")
    .Replace("'", "")
    .Replace(".", "")
    .Replace(" ", "")
    .Trim();  // 6 аллокаций строк!
```

**Решение:**
```csharp
private static readonly Regex NormalizePattern = new(@"[\s'.-]", RegexOptions.Compiled);

public static string Normalize(string text)
{
    return NormalizePattern.Replace(text.ToLowerInvariant(), "").Trim();
}
```

### 2.3 Дублирование кода загрузки токена

**Файлы:**
- `AddCommandHandler.cs:67-95`
- `UpdateCommandHandler.cs:85-119`

**Проблема:** Идентичный код загрузки токена и регистрации IGeniProfileClient/IGeniPhotoClient повторяется в обоих обработчиках.

**Решение:** Создать extension method или helper:
```csharp
public static class GeniServiceExtensions
{
    public static IServiceCollection AddGeniClients(
        this IServiceCollection services,
        string tokenFile,
        bool dryRun)
    {
        services.AddSingleton(sp =>
        {
            var token = LoadAndValidateToken(tokenFile);
            return new GeniClientFactory(
                sp.GetRequiredService<IHttpClientFactory>(),
                token.AccessToken,
                dryRun);
        });

        services.AddSingleton<IGeniProfileClient>(sp =>
            sp.GetRequiredService<GeniClientFactory>().CreateProfileClient(
                sp.GetRequiredService<ILogger<GeniProfileClient>>()));

        services.AddSingleton<IGeniPhotoClient>(sp =>
            sp.GetRequiredService<GeniClientFactory>().CreatePhotoClient(
                sp.GetRequiredService<ILogger<GeniPhotoClient>>()));

        return services;
    }
}
```

### 2.4 Дублирование JSON десериализации отчётов

**Файлы:**
- `AddCommandHandler.cs:128-142`
- `UpdateCommandHandler.cs:149-163`

**Решение:**
```csharp
public static class WaveReportLoader
{
    public static WaveHighConfidenceReport? LoadReport(string jsonContent, ILogger logger)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Try wrapped format first
        var wrapper = JsonSerializer.Deserialize<WaveCompareJsonWrapper>(jsonContent, options);
        if (wrapper?.Report != null)
            return wrapper.Report;

        // Try direct format
        return JsonSerializer.Deserialize<WaveHighConfidenceReport>(jsonContent, options);
    }
}
```

---

## 3. ПОТЕНЦИАЛЬНЫЕ БАГИ

### 3.1 Пустой catch block проглатывает исключения

**Файл:** `GedcomGeniSync.ApiClient/Services/GeniAuthClient.cs:205-208`

```csharp
private static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch
    {
        // Пользователь не узнает, что браузер не открылся!
    }
}
```

**Решение:**
```csharp
catch (Exception ex)
{
    _logger?.LogWarning(ex, "Failed to open browser. Please navigate to: {Url}", url);
    Console.WriteLine($"Please open this URL in your browser: {url}");
}
```

### 3.2 Substring без проверки границ

**Файлы:**
- `UpdateExecutor.cs:456-472`
- `AddExecutor.cs:356-372`

```csharp
var colonIndex = profileId.LastIndexOf(':');
if (colonIndex >= 0)
{
    var numericId = profileId.Substring(colonIndex + 1);  // Может вернуть пустую строку!
    return $"g{numericId}";  // Результат: "g" без числа
}
```

**Решение:**
```csharp
if (colonIndex >= 0 && colonIndex + 1 < profileId.Length)
{
    var numericId = profileId.Substring(colonIndex + 1);
    if (!string.IsNullOrEmpty(numericId))
        return $"g{numericId}";
}
```

### 3.3 GEDCOM charset без значения

**Файл:** `GedcomGeniSync.Core/Services/GedcomLoader.cs:1256-1259`

```csharp
if (line.StartsWith("1 CHAR ", StringComparison.OrdinalIgnoreCase))
{
    var charsetName = line.Substring(7).Trim();
    // charsetName может быть пустым если строка "1 CHAR "
    return charsetName.ToUpperInvariant() switch { ... };
}
```

**Решение:**
```csharp
var charsetName = line.Substring(7).Trim();
if (string.IsNullOrEmpty(charsetName))
{
    _logger?.LogWarning("CHAR tag without value, defaulting to UTF-8");
    return Encoding.UTF8;
}
```

---

## 4. ОПТИМИЗАЦИИ ПРОИЗВОДИТЕЛЬНОСТИ

### 4.1 LINQ: Множественные вызовы Count() на IEnumerable

**Файл:** `WaveCompareLogFormatter.cs:231,240,242`

```csharp
sb.AppendLine($"[{group.Key}] ({group.Count()} issues)");
if (group.Count() > 20)
{
    sb.AppendLine($"  ... and {group.Count() - 20} more");
}
```

**Решение:**
```csharp
var count = group.Count();  // Материализуем один раз
sb.AppendLine($"[{group.Key}] ({count} issues)");
if (count > 20)
    sb.AppendLine($"  ... and {count - 20} more");
```

### 4.2 Последовательная загрузка фото вместо параллельной

**Файл:** `MyHeritagePhotoService.cs:165-178`

```csharp
foreach (var url in urls.Where(IsMyHeritageUrl))
{
    var result = await DownloadPhotoAsync(url);  // Последовательно!
}
```

**Решение:**
```csharp
public async Task<List<PhotoDownloadResult>> DownloadPhotosAsync(IEnumerable<string> urls)
{
    var tasks = urls
        .Where(IsMyHeritageUrl)
        .Select(DownloadPhotoAsync);

    var results = await Task.WhenAll(tasks);
    return results.Where(r => r != null).ToList()!;
}
```

### 4.3 Intersect/Union с последующим Count()

**Файл:** `FuzzyMatcherService.cs:550-551`

```csharp
var intersection = sourceTokens.Intersect(targetTokens).Count();
var union = sourceTokens.Union(targetTokens).Count();
```

**Решение:**
```csharp
// Оптимизация для Jaccard similarity
var intersectionCount = 0;
foreach (var token in sourceTokens)
{
    if (targetTokens.Contains(token))
        intersectionCount++;
}
var unionCount = sourceTokens.Count + targetTokens.Count - intersectionCount;
return unionCount > 0 ? (double)intersectionCount / unionCount : 0.0;
```

### 4.4 ToList() для получения только первого элемента

**Файл:** `PersonFieldComparer.cs:166`

```csharp
var newPhotos = sourcePhotos.Except(destPhotos).ToList();
if (newPhotos.Count > 0)
{
    differences.Add(new FieldDiff { SourceValue = newPhotos[0] ... });
}
```

**Решение:**
```csharp
var newPhoto = sourcePhotos.Except(destPhotos).FirstOrDefault();
if (newPhoto != null)
{
    differences.Add(new FieldDiff { SourceValue = newPhoto ... });
}
```

---

## 5. УПРОЩЕНИЕ КОДА

### 5.1 Дублирование проверки года рождения/смерти

**Файл:** `WaveMappingValidator.cs:78-133`

Идентичный код проверки разницы лет для BirthYear и DeathYear.

**Решение:**
```csharp
private void ValidateYearDifference(
    int? sourceYear,
    int? destYear,
    string fieldName,
    PersonMapping mapping,
    List<ValidationIssue> issues)
{
    if (!sourceYear.HasValue || !destYear.HasValue)
        return;

    var diff = Math.Abs(sourceYear.Value - destYear.Value);
    var (severity, message) = diff switch
    {
        > 15 => (Severity.High, $"{fieldName} differs by {diff} years"),
        > 5 => (Severity.Medium, $"{fieldName} differs by {diff} years"),
        _ => ((Severity?)null, (string?)null)
    };

    if (severity.HasValue)
    {
        issues.Add(new ValidationIssue
        {
            Severity = severity.Value,
            Message = $"{message} ({sourceYear} vs {destYear})"
        });
    }
}

// Использование:
ValidateYearDifference(source.BirthYear, dest.BirthYear, "Birth year", mapping, issues);
ValidateYearDifference(source.DeathYear, dest.DeathYear, "Death year", mapping, issues);
```

### 5.2 Громоздкий CleanProfileId

**Файл:** `UpdateExecutor.cs:450-484`

35 строк вложенных if-else для очистки ID профиля.

**Решение:**
```csharp
private static string CleanProfileId(string profileId)
{
    if (string.IsNullOrWhiteSpace(profileId))
        return profileId;

    // Extract numeric part
    var id = profileId.Contains(':')
        ? profileId[(profileId.LastIndexOf(':') + 1)..]
        : profileId.Replace("profile-", "", StringComparison.OrdinalIgnoreCase);

    // Ensure g prefix
    return id.StartsWith('g') ? id : $"g{id}";
}
```

### 5.3 Повторяющаяся проверка null для имён

**Файл:** `GedcomLoader.cs:160-177`

```csharp
if (!string.IsNullOrEmpty(pieces.GivenName))
{
    var givenVariants = ExtractNameVariants(pieces.GivenName);
    foreach (var variant in givenVariants)
    {
        if (!nameVariantsBuilder.Contains(variant))
            nameVariantsBuilder.Add(variant);
    }
}
// Повторяется для Surname, Nickname...
```

**Решение:**
```csharp
private void AddNameVariantsIfPresent(string? name, ImmutableList<string>.Builder builder)
{
    if (string.IsNullOrEmpty(name)) return;

    foreach (var variant in ExtractNameVariants(name))
    {
        if (!builder.Contains(variant))
            builder.Add(variant);
    }
}

// Использование:
AddNameVariantsIfPresent(pieces.GivenName, nameVariantsBuilder);
AddNameVariantsIfPresent(pieces.Surname, nameVariantsBuilder);
AddNameVariantsIfPresent(pieces.Nickname, nameVariantsBuilder);
```

### 5.4 Дублирование NameVariantsService методов

**Файл:** `NameVariantsService.cs:104-142` vs `147-176`

Методы `AreEquivalent()` и `AreEquivalentSurnames()` почти идентичны.

**Решение:**
```csharp
public bool AreEquivalent(string name1, string name2)
    => IsNormalizedMatch(name1, name2, _givenNameGroups);

public bool AreEquivalentSurnames(string name1, string name2)
    => IsNormalizedMatch(name1, name2, _surnameGroups);

private bool IsNormalizedMatch(string? name1, string? name2,
    Dictionary<string, HashSet<string>> groups)
{
    if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
        return false;

    return CheckInGroup(name1, name2, groups) ||
           CheckInGroup(Transliterate(name1), Transliterate(name2), groups);
}

private bool CheckInGroup(string name1, string name2,
    Dictionary<string, HashSet<string>> groups)
{
    var norm1 = name1.ToLowerInvariant().Trim();
    var norm2 = name2.ToLowerInvariant().Trim();

    return norm1 == norm2 ||
           (groups.TryGetValue(norm1, out var g1) && g1.Contains(norm2)) ||
           (groups.TryGetValue(norm2, out var g2) && g2.Contains(norm1));
}
```

---

## 6. ДОПОЛНИТЕЛЬНЫЕ РЕКОМЕНДАЦИИ

### 6.1 Паттерны API клиента

**GeniProfileClient.cs** содержит много дублирования:
- 6 методов AddXxx() с почти идентичной структурой
- Каждый метод: DryRun check → ThrottleAsync → URL → CreateClient → ExecuteWithRetry → Deserialize

**Рекомендация:** Использовать шаблонный метод:

```csharp
private async Task<T?> ExecutePostAsync<T>(
    string urlPath,
    FormUrlEncodedContent content,
    string operationName,
    params object[] logParams) where T : class
{
    if (DryRun)
    {
        Logger.LogInformation("[DRY-RUN] Would {Operation}", operationName);
        return default;
    }

    await ThrottleAsync();

    var url = $"{BaseUrl}/{urlPath}";
    Logger.LogDebug("POST {Url}", url);

    using var client = CreateClient();
    var response = await ExecuteWithRetryAsync(() => client.PostAsync(url, content));
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<T>();
}
```

### 6.2 Кэширование транслитерации

Метод `Transliterate()` вызывается многократно для одних и тех же имён.

**Рекомендация:** Кэшировать результаты в PersonRecord:
```csharp
public record PersonRecord
{
    // Существующие поля...

    // Добавить кэшированные транслитерации
    public string? TransliteratedFirstName { get; init; }
    public string? TransliteratedLastName { get; init; }
}
```

### 6.3 ImmutableList.Contains() - линейный поиск

**Файл:** `GedcomLoader.cs:162-186`

```csharp
if (!nameVariantsBuilder.Contains(variant))  // O(n) каждый раз!
    nameVariantsBuilder.Add(variant);
```

**Рекомендация:** Использовать HashSet для отслеживания добавленных:
```csharp
var addedVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var variant in ExtractNameVariants(name))
{
    if (addedVariants.Add(variant))
        nameVariantsBuilder.Add(variant);
}
```

---

## 7. ПРИОРИТИЗАЦИЯ ИСПРАВЛЕНИЙ

### Фаза 1: Критические (1-2 дня)
1. [ ] Исправить `.Result` блокировки в command handlers
2. [ ] Вынести Regex паттерны в статические compiled поля
3. [ ] Вынести словари месяцев в статические поля

### Фаза 2: Высокий приоритет (3-5 дней)
4. [ ] Рефакторинг дублирования загрузки токена
5. [ ] Рефакторинг дублирования JSON десериализации
6. [ ] Исправить пустой catch block
7. [ ] Оптимизировать цепочки String.Replace

### Фаза 3: Средний приоритет (5-7 дней)
8. [ ] Параллельная загрузка фото
9. [ ] Оптимизация LINQ операций
10. [ ] Упрощение ValidateYearDifference
11. [ ] Рефакторинг NameVariantsService
12. [ ] Упрощение CleanProfileId

### Фаза 4: Низкий приоритет (по необходимости)
13. [ ] Шаблонный метод для API вызовов
14. [ ] Кэширование транслитерации
15. [ ] Замена Contains на HashSet

---

## 8. ОЦЕНКА УЛУЧШЕНИЙ

| Метрика | До | После (ожидаемо) |
|---------|-----|------------------|
| Строк дублированного кода | ~400 | ~100 |
| Потенциальные deadlock точки | 6 | 0 |
| Regex компиляций в горячих путях | ~15 | 0 |
| Словарей создаваемых в циклах | 2 | 0 |
| Время загрузки GEDCOM 50k | ~15 сек | ~8 сек |
| Время wave-compare | ~30 сек | ~18 сек |

---

*Отчёт сгенерирован: 2025-12-20*
