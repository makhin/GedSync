# Combined Score Implementation - Complete

## Дата: 2025-12-14

## Проблема (Решена ✅)

### Исходная проблема
Пользователь обнаружил, что при наличии нескольких кандидатов на роль супруга система не сравнивала их всех, а просто брала первую подходящую семью по структурному score.

**Пример проблемного сценария:**
```
Source: Александр Махин (@I500002@) [уже сопоставлен]

Семья 1: @F500003@
  Husband: Александр Махин (уже сопоставлен)
  Wife: Магдалина Зайцева (*1975) @I500005@ [нужно сопоставить]

Семья 2: @F500004@
  Husband: Александр Махин (уже сопоставлен)
  Wife: Дарья Клименко (*1985) @I500006@ [нужно сопоставить]

Destination: У Александра Махина две семьи в Geni:

Dest Family 1: @F6000000207133980751@
  Husband: Александр Махин (сопоставлен)
  Wife: Магдалина Зайцева (*1975) @I6000000207133980231@
  Structure Score: 60

Dest Family 2: @F6000000207133980774@
  Husband: Александр Махин (сопоставлен)
  Wife: Дарья Клименко (*1985) @I6000000207133980738@
  Structure Score: 60
```

**Старое поведение:**
- FamilyMatcher выбирал семью только по структурному score
- Обе семьи имели одинаковый структурный score = 60
- Выбиралась первая попавшаяся, даже если персональное сходство было низким

**Требуемое поведение:**
- Учитывать персональное сходство супругов при выборе семьи
- Если структурные scores одинаковые, выбирать семью с лучшим персональным сходством

## Решение: Комбинированный Score

### Принцип работы

Вместо использования только структурного score (на основе уже сопоставленных членов семьи), теперь вычисляется **комбинированный score**, который учитывает:

1. **Структурный score (40%)** - на основе уже сопоставленных членов семьи
2. **Персональный score мужа (30%)** - fuzzy matching score между source и dest мужем
3. **Персональный score жены (30%)** - fuzzy matching score между source и dest женой

Если только один супруг нужно сопоставить (второй уже сопоставлен), распределение:
- **40%** структурный score
- **60%** персональный score единственного супруга

### Изменения в коде

#### 1. FamilyMatcher.cs

**Добавлена зависимость от IFuzzyMatcherService:**
```csharp
private readonly IFuzzyMatcherService? _fuzzyMatcher;

public FamilyMatcher(
    ILogger<FamilyMatcher>? logger = null,
    IFuzzyMatcherService? fuzzyMatcher = null)
{
    _logger = logger;
    _fuzzyMatcher = fuzzyMatcher;
}
```

**Добавлен метод CalculateCombinedScore:**
```csharp
private int CalculateCombinedScore(
    FamilyRecord sourceFamily,
    FamilyRecord destFamily,
    IReadOnlyDictionary<string, PersonMapping> mappings,
    TreeGraph? sourceTree,
    TreeGraph? destTree,
    int structureScore,
    List<ScoreComponent> scoreBreakdown)
{
    // Если нет FuzzyMatcher или деревьев, используем только структурный score
    if (_fuzzyMatcher == null || sourceTree == null || destTree == null)
        return structureScore;

    int husbandScore = 0;
    int wifeScore = 0;
    bool hasHusbandToMatch = false;
    bool hasWifeToMatch = false;

    // Вычисляем персональный score для мужа (если еще не сопоставлен)
    if (sourceFamily.HusbandId != null &&
        destFamily.HusbandId != null &&
        !mappings.ContainsKey(sourceFamily.HusbandId))
    {
        hasHusbandToMatch = true;
        var result = _fuzzyMatcher.Compare(
            sourceTree.PersonsById[sourceFamily.HusbandId],
            destTree.PersonsById[destFamily.HusbandId]);
        husbandScore = (int)result.Score;

        scoreBreakdown.Add(new ScoreComponent
        {
            Component = "Husband Personal Score",
            Points = husbandScore,
            Description = $"Personal similarity: {husbandScore}%"
        });
    }

    // Аналогично для жены...

    // Вычисляем комбинированный score
    if (hasHusbandToMatch && hasWifeToMatch)
    {
        // 40% structure, 30% husband, 30% wife
        int combinedScore = (int)(
            structureScore * 0.4 +
            husbandScore * 0.3 +
            wifeScore * 0.3);

        scoreBreakdown.Add(new ScoreComponent
        {
            Component = "Combined Total",
            Points = combinedScore,
            Description = $"Structure: {structureScore}*0.4 + Husband: {husbandScore}*0.3 + Wife: {wifeScore}*0.3"
        });

        return combinedScore;
    }
    else if (hasHusbandToMatch || hasWifeToMatch)
    {
        // 40% structure, 60% for single spouse
        int spouseScore = hasHusbandToMatch ? husbandScore : wifeScore;
        int combinedScore = (int)(
            structureScore * 0.4 +
            spouseScore * 0.6);

        string spouseName = hasHusbandToMatch ? "Husband" : "Wife";
        scoreBreakdown.Add(new ScoreComponent
        {
            Component = "Combined Total",
            Points = combinedScore,
            Description = $"Structure: {structureScore}*0.4 + {spouseName}: {spouseScore}*0.6"
        });

        return combinedScore;
    }

    // Оба супруга уже сопоставлены - используем только структурный score
    return structureScore;
}
```

**Модификация FindMatchingFamilyWithLog:**
```csharp
foreach (var destFamily in destFamiliesList)
{
    var (structureScore, hasConflict, scoreBreakdown, conflictReason) =
        CalculateFamilyMatchScoreDetailed(...);

    // Вычисляем комбинированный score с учетом персональных совпадений супругов
    int totalScore = CalculateCombinedScore(
        sourceFamily,
        destFamily,
        mappings,
        sourceTree,
        destTree,
        structureScore,
        scoreBreakdown);

    // Используем totalScore вместо structureScore
    if (!hasConflict && totalScore > bestScore)
    {
        bestScore = totalScore;
        bestFamily = destFamily;
    }
}
```

#### 2. WaveCompareService.cs

**Передача FuzzyMatcher в FamilyMatcher:**
```csharp
public WaveCompareService(
    IFuzzyMatcherService fuzzyMatcher,
    ILogger<WaveCompareService> logger)
{
    _fuzzyMatcher = fuzzyMatcher;
    _logger = logger;
    _treeIndexer = new TreeIndexer(logger: null);
    _familyMatcher = new FamilyMatcher(logger: null, fuzzyMatcher: _fuzzyMatcher);  // ← Изменено
    _validator = new WaveMappingValidator(logger: null);
}
```

## Результаты тестирования

### Тест с max-level=3

**Команда:**
```bash
./GedcomGeniSync.Cli/bin/Debug/net8.0/GedcomGeniSync.Cli.exe wave-compare \
  --source myheritage.ged \
  --destination geni.ged \
  --output results_combined_score.json \
  --anchor-source I500002 \
  --anchor-destination I6000000206529622827 \
  --max-level 3 \
  --verbose \
  --detailed-log detailed_combined_score.log
```

**Результат: ✅ УСПЕШНО**

#### Проверка корректности сопоставления

**1. Магдалина Зайцева (1975) - ПРАВИЛЬНО**
```json
{
  "sourceId": "@I500005@",
  "destinationId": "@I6000000207133980231@",
  "matchScore": 100,
  "level": 1,
  "foundVia": 1  // Spouse
}
```

Source: Магдалина Шотаевна Зайцева (*1975)
Destination: Магдалина Шотаевна Зайцева (*1975) - ✅ ПРАВИЛЬНО!

**2. Дарья Клименко (1985) - ПРАВИЛЬНО**
```json
{
  "sourceId": "@I500006@",
  "destinationId": "@I6000000207133980738@",
  "matchScore": 100,
  "level": 1,
  "foundVia": 1  // Spouse
}
```

Source: Дарья Владимировна Клименко (*1985)
Destination: Дарья Владимировна Клименко (*1985) - ✅ ПРАВИЛЬНО!

### Детальный лог показывает комбинированные scores

```
Family: @F500003@
Structure: H:Александр Владимирович Махин (*1974) + W:Магдалина Шотаевна Зайцева (*1975) → 2 children
✓ MATCHED → @F6000000207133980751@ (Score: 84)
  Score Breakdown:
    • Husband Match: +50 (Husband @I500002@ already mapped to @I6000000206529622827@)
    • Wife Present: +10 (Both families have wife (not yet mapped))
    • Wife Personal Score: +100 (Personal similarity: 100%)
    • Combined Total: +84 (Structure: 60*0.4 + Wife: 100*0.6)
```

**Расчет:**
- Structure score: 60 (Husband Match: 50 + Wife Present: 10)
- Wife personal score: 100 (Магдалина vs Магдалина = 100%)
- Combined: 60 * 0.4 + 100 * 0.6 = 24 + 60 = **84**

Аналогично для Дарьи - обе семьи получили score 84, так как:
- Структурные scores одинаковые (60)
- Персональные scores обеих жен идеальные (100%)

## Влияние на результаты

### До (без комбинированного score)
- ❌ Возможность неправильного выбора семьи при одинаковых структурных scores
- ❌ Супруг из первой попавшейся семьи мог быть выбран даже при низком персональном сходстве

### После (с комбинированным score)
- ✅ Персональное сходство супругов учитывается при выборе семьи
- ✅ Семья с лучшим персональным score выбирается, даже если структурные scores одинаковые
- ✅ Более точные сопоставления в случаях, когда у одного человека несколько семей

## Преимущества решения

1. **Простота реализации** - локальные изменения только в FamilyMatcher и WaveCompareService
2. **Не ломает существующую архитектуру** - backward compatible
3. **Прозрачность** - score breakdown в логах показывает как вычислялся комбинированный score
4. **Эффективность** - не требует множественных проходов или сложной логики

## Возможные улучшения

### Краткосрочные
1. **Логирование кандидатов** - добавить в detailed log список всех рассмотренных кандидатов с их scores
2. **Настройка весов** - позволить пользователю настраивать веса (40%/30%/30%) через параметры

### Среднесрочные
1. **Solution 1 из SPOUSE_MATCHING_IMPROVEMENT.md** - для еще большей точности можно реализовать множественных кандидатов
2. **Учет детей** - добавить персональные scores детей в комбинированный score

## Статус

- ✅ **Реализовано**: Комбинированный score в FamilyMatcher
- ✅ **Протестировано**: Правильное сопоставление Магдалины и Дарьи
- ✅ **Задокументировано**: Создана полная документация
- ✅ **Готово к production**

---

**Автор**: Claude Sonnet 4.5
**Дата**: 2025-12-14
**Файлы изменены**:
- `GedcomGeniSync.Core/Services/Wave/FamilyMatcher.cs` - добавлен комбинированный score
- `GedcomGeniSync.Core/Services/Wave/WaveCompareService.cs` - передача fuzzy matcher

**Связанные документы**:
- [SPOUSE_MATCHING_IMPROVEMENT.md](SPOUSE_MATCHING_IMPROVEMENT.md) - анализ проблемы и предложенные решения
- [WAVE_THRESHOLD_FIX.md](WAVE_THRESHOLD_FIX.md) - исправление порогов
- [WAVE_THRESHOLD_ANALYSIS.md](WAVE_THRESHOLD_ANALYSIS.md) - анализ проблемы с порогами
