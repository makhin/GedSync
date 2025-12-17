# План удаления команд compare и sync

## Обзор

Удаление команд `compare` и `sync` из CLI, включая связанные сервисы, модели и тесты.
**Критически важно:** сохранить код, используемый командами `wave-compare`, `add`, `update`, `analyze`, `auth`, `profile`.

---

## 1. Анализ зависимостей

### Команды, которые ОСТАЮТСЯ:
| Команда | Описание |
|---------|----------|
| `wave-compare` | Продвинутое сравнение с волновым алгоритмом |
| `add` | Добавление профилей в Geni |
| `update` | Обновление профилей в Geni |
| `analyze` | Анализ GEDCOM файла |
| `auth` | OAuth аутентификация |
| `profile` | Получение профиля пользователя |

### Общие зависимости (НЕ удалять):

| Компонент | Используется |
|-----------|--------------|
| `PersonFieldComparer` / `IPersonFieldComparer` | wave-compare |
| `CompareModels.cs` (NodeToUpdate, NodeToAdd, PersonData, CompareRelationType) | wave-compare |
| `GeniProfileClient` / `IGeniProfileClient` | add, update, profile |
| `GeniApiClient` / `IGeniApiClient` | sync удаляется, но add/update/profile остаются |
| `MyHeritagePhotoService` / `IMyHeritagePhotoService` | add, update |
| `FuzzyMatcherService` / `IFuzzyMatcherService` | wave-compare |
| `GedcomLoader` / `IGedcomLoader` | wave-compare, analyze, add, update |
| `NameVariantsService` / `INameVariantsService` | fuzzy matching |
| `ConfigurationService` / `IConfigurationLoader` | все команды |

---

## 2. Файлы для УДАЛЕНИЯ

### 2.1 CLI Commands (2 файла)
```
GedcomGeniSync.Cli/Commands/SyncCommandHandler.cs
GedcomGeniSync.Cli/Commands/CompareCommandHandler.cs
```

### 2.2 Core Services - Compare (6 файлов)
Удалить все, КРОМЕ PersonFieldComparer:
```
GedcomGeniSync.Core/Services/Compare/GedcomCompareService.cs
GedcomGeniSync.Core/Services/Compare/IGedcomCompareService.cs
GedcomGeniSync.Core/Services/Compare/IndividualCompareService.cs
GedcomGeniSync.Core/Services/Compare/IIndividualCompareService.cs
GedcomGeniSync.Core/Services/Compare/FamilyCompareService.cs
GedcomGeniSync.Core/Services/Compare/IFamilyCompareService.cs
GedcomGeniSync.Core/Services/Compare/MappingValidationService.cs
GedcomGeniSync.Core/Services/Compare/IMappingValidationService.cs
```

**ОСТАВИТЬ:**
```
GedcomGeniSync.Core/Services/Compare/PersonFieldComparer.cs      ← используется wave-compare
GedcomGeniSync.Core/Services/Compare/IPersonFieldComparer.cs     ← используется wave-compare
```

### 2.3 Core Services - Sync (4 файла)
```
GedcomGeniSync.Core/Services/SyncService.cs
GedcomGeniSync.Core/Services/Interfaces/ISyncService.cs
GedcomGeniSync.Core/Services/SyncStateManager.cs
GedcomGeniSync.Core/Services/Interfaces/ISyncStateManager.cs
```

### 2.4 Core Models - Sync (4 файла)
```
GedcomGeniSync.Core/Models/SyncOptions.cs
GedcomGeniSync.Core/Models/SyncReport.cs
GedcomGeniSync.Core/Models/SyncState.cs
GedcomGeniSync.Core/Models/SyncStatistics.cs
```

### 2.5 Tests (6 файлов)
```
GedcomGeniSync.Tests/SyncServiceRelativeProcessingTests.cs
GedcomGeniSync.Tests/Services/Compare/FamilyCompareServiceTests.cs
GedcomGeniSync.Tests/Services/Compare/MappingValidationServiceTests.cs
GedcomGeniSync.Tests/Services/Compare/FamilyPrioritizationTests.cs
GedcomGeniSync.Tests/Services/Compare/AmbiguousMatchResolutionTests.cs
GedcomGeniSync.Tests/Services/Compare/FamilyChildrenFuzzyMatchTests.cs
```

**ОСТАВИТЬ тесты:**
```
GedcomGeniSync.Tests/Services/Compare/PersonFieldComparerTests.cs  ← для PersonFieldComparer
GedcomGeniSync.Tests/PersonFieldComparerTests.cs                   ← для PersonFieldComparer (дубликат?)
```

---

## 3. Файлы для МОДИФИКАЦИИ

### 3.1 Program.cs
Удалить регистрацию команд:
```csharp
// УДАЛИТЬ эти строки:
new SyncCommandHandler(startup),
new CompareCommandHandler(startup),
```

**Файл:** `GedcomGeniSync.Cli/Program.cs`

---

## 4. Порядок выполнения

### Фаза 1: Удаление команд CLI
1. Удалить `SyncCommandHandler.cs`
2. Удалить `CompareCommandHandler.cs`
3. Обновить `Program.cs` - убрать регистрацию команд

### Фаза 2: Удаление Sync сервисов и моделей
4. Удалить `SyncService.cs` и `ISyncService.cs`
5. Удалить `SyncStateManager.cs` и `ISyncStateManager.cs`
6. Удалить модели: `SyncOptions.cs`, `SyncReport.cs`, `SyncState.cs`, `SyncStatistics.cs`

### Фаза 3: Удаление Compare сервисов (кроме PersonFieldComparer)
7. Удалить `GedcomCompareService.cs` и `IGedcomCompareService.cs`
8. Удалить `IndividualCompareService.cs` и `IIndividualCompareService.cs`
9. Удалить `FamilyCompareService.cs` и `IFamilyCompareService.cs`
10. Удалить `MappingValidationService.cs` и `IMappingValidationService.cs`

### Фаза 4: Удаление тестов
11. Удалить `SyncServiceRelativeProcessingTests.cs`
12. Удалить `Services/Compare/FamilyCompareServiceTests.cs`
13. Удалить `Services/Compare/MappingValidationServiceTests.cs`
14. Удалить `Services/Compare/FamilyPrioritizationTests.cs`
15. Удалить `Services/Compare/AmbiguousMatchResolutionTests.cs`
16. Удалить `Services/Compare/FamilyChildrenFuzzyMatchTests.cs`

### Фаза 5: Проверка
17. Собрать проект: `dotnet build`
18. Запустить тесты: `dotnet test`
19. Проверить работу оставшихся команд

---

## 5. Сводка по файлам

| Категория | Удалить | Оставить |
|-----------|---------|----------|
| CLI Commands | 2 | 6 |
| Compare Services | 8 | 2 (PersonFieldComparer) |
| Sync Services | 4 | 0 |
| Sync Models | 4 | 0 |
| Tests | 6 | 2 (PersonFieldComparer tests) |
| **ИТОГО** | **24 файла** | - |

---

## 6. Риски и проверки

### Проверить перед удалением каждого файла:
```bash
# Проверить, что файл не используется нигде кроме удаляемых компонентов
grep -r "ИмяКласса" --include="*.cs" | grep -v "удаляемые_файлы"
```

### После удаления:
1. `dotnet build` должен успешно завершиться
2. `dotnet test` должен пройти (для оставшихся тестов)
3. Команды `wave-compare`, `add`, `update`, `analyze`, `auth`, `profile` должны работать

---

## 7. CompareModels.cs - особое внимание

Файл `GedcomGeniSync.Core/Models/CompareModels.cs` **НЕ УДАЛЯТЬ** - используется:
- `NodeToUpdate` - WaveCompareCommandHandler (строка 236)
- `NodeToAdd` - WaveCompareCommandHandler (строка 263)
- `PersonData` - WaveCompareCommandHandler (строка 329)
- `CompareRelationType` - WaveCompareCommandHandler (строки 287, 299, 304, 309, 315, 321)
- `FieldDiff` - используется в NodeToUpdate

Можно рассмотреть рефакторинг в будущем: перенести используемые типы в Wave модели.
