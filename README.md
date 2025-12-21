# GedcomGeniSync

Инструмент для синхронизации генеалогических данных из GEDCOM файлов (MyHeritage, etc.) в Geni.com.

## Возможности

- **Fuzzy matching** — нечёткое сравнение имён с поддержкой:
  - Транслитерации (кириллица ↔ латиница)
  - Словаря эквивалентов имён (Иван = Ivan = John = Johann)
  - Девичьих фамилий
  - Неточных дат (±2 года)
  
- **BFS синхронизация** — обход дерева от якоря, поиск совпадений в Geni, создание недостающих профилей

- **Dry-run режим** — предпросмотр изменений без реального создания

- **Resume support** — сохранение состояния для продолжения после сбоев

- **Фото-кэш и сравнение** — локальный кэш, сравнение по содержимому и upload из кэша

## Установка

```bash
git clone https://github.com/YOUR_USERNAME/GedcomGeniSync.git
cd GedcomGeniSync
dotnet restore
dotnet build
```

## Использование

### 1. Получение Geni API токена

1. Зарегистрируйте приложение: https://www.geni.com/platform/developer/apps
2. Запустите интерактивную авторизацию (Desktop OAuth flow):

```bash
dotnet run --project GedcomGeniSync.Cli -- auth \
  --app-key YOUR_APP_KEY \
  --token-file geni_token.json
```

   **Как это работает:**
   - Откроется браузер с формой авторизации Geni
   - После входа и подтверждения, вас перенаправит на страницу успеха
   - Скопируйте **полный URL** из адресной строки браузера
   - Вставьте URL в консоль приложения
   - Токен будет автоматически извлечён и сохранён в `geni_token.json`

   **Примечание:** Используется [Desktop OAuth Flow](https://www.geni.com/platform/developer/help/oauth_desktop?version=1) (Implicit Grant). Для этого flow не нужен `app_secret`, только `app_key`.

   Переменную `GENI_APP_KEY` можно задать в окружении вместо передачи через параметры. По умолчанию токен сохраняется в `geni_token.json`, затем его можно использовать в других командах через `--token`, `GENI_ACCESS_TOKEN` или просто указав путь к файлу через `--token-file`.

### 2. Конфигурационный файл (опционально)

Вместо указания всех параметров через CLI, можно создать конфигурационный файл:

```bash
# Скопируйте пример конфигурации
cp gedsync.example.json gedsync.json
# или
cp gedsync.example.yaml gedsync.yaml

# Отредактируйте под свои нужды
nano gedsync.json
```

Приложение автоматически загрузит конфигурацию из одного из файлов:
- `gedsync.json`, `gedsync.yaml`, `gedsync.yml`
- `.gedsync.json`, `.gedsync.yaml`, `.gedsync.yml`

Или укажите путь явно:
```bash
dotnet run --project GedcomGeniSync.Cli -- sync --config my-config.yaml ...
```

**Приоритет настроек**: CLI параметры > конфигурационный файл > значения по умолчанию

Пример `gedsync.yaml`:
```yaml
matching:
  matchThreshold: 75
  maxBirthYearDifference: 5

sync:
  maxDepth: 10
  dryRun: true

paths:
  stateFile: sync_state.json
  reportFile: sync_report.json

logging:
  verbose: false

photo:
  enabled: true
  cacheDirectory: "./photos"
  downloadOnLoad: true
  similarityThreshold: 0.95
  maxConcurrentDownloads: 4
```

Подробнее о кэше: `docs/PHOTO_CACHE.md`

### 3. Анализ GEDCOM файла

```bash
dotnet run --project GedcomGeniSync.Cli -- analyze --gedcom family.ged --anchor @I123@
```

### 4. Wave Compare (сравнение двух GEDCOM файлов)

Wave Compare использует алгоритм BFS для сопоставления персон между двумя GEDCOM файлами.

**Стандартные параметры для тестирования:**

```bash
# Быстрый запуск через скрипт
.\test-wave-compare.bat

# Или полная команда
.\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe wave-compare \
  --source myheritage.ged \
  --destination geni.ged \
  --anchor-source I500002 \
  --anchor-destination I6000000206529622827 \
  --output results.json \
  --max-level 1000 \
  --ignore-photos
```

**Стандартные якоря для тестирования:**
- `--anchor-source I500002` (Александр Владимирович Махин, *1974)
- `--anchor-destination I6000000206529622827` (тот же человек в Geni)

**Дополнительные параметры:**
- `--detailed-log detailed.log` — детальный лог сопоставлений
- `--verbose` — подробный вывод
- `--threshold-strategy` — стратегия порога: fixed, adaptive, aggressive, conservative
- `--base-threshold` — базовый порог сопоставления (0-100, по умолчанию 60)

### 5. Тест matching логики

```bash
dotnet run --project GedcomGeniSync.Cli -- test-match
```

### 6. Синхронизация (dry-run)

```bash
# С использованием конфигурационного файла
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token-file geni_token.json

# Или с явным указанием параметров
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token-file geni_token.json \
  --threshold 70 \
  --verbose
```

### 7. Реальная синхронизация

```bash
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token-file geni_token.json \
  --dry-run false \
  --max-depth 10
```

## Параметры

### CLI параметры

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `--config` | Путь к конфигурационному файлу (JSON/YAML) | auto-detect |
| `--gedcom` | Путь к GEDCOM файлу | (обязательно) |
| `--anchor-ged` | GEDCOM ID якоря (напр. @I123@) | (обязательно) |
| `--anchor-geni` | Geni ID якоря | (обязательно) |
| `--token` | Geni API токен (или env GENI_ACCESS_TOKEN) | - |
| `--token-file` | Путь к сохранённому токену (по умолчанию `geni_token.json`) | geni_token.json |
| `--dry-run` | Режим предпросмотра | true |
| `--threshold` | Порог совпадения (0-100) | 70 |
| `--max-depth` | Максимальная глубина BFS | unlimited |
| `--state-file` | Файл состояния для resume | sync_state.json |
| `--report` | Файл отчёта | sync_report.json |
| `--given-names-csv` | CSV с вариантами имён | - |
| `--surnames-csv` | CSV с вариантами фамилий | - |
| `--verbose` | Подробный вывод | false |

### Конфигурационный файл

Все параметры можно задать в конфигурационном файле (JSON или YAML). См. примеры:
- `gedsync.example.json` — пример конфигурации в формате JSON
- `gedsync.example.yaml` — пример конфигурации в формате YAML

**Разделы конфигурации:**

#### `matching` — настройки алгоритма сопоставления
- `firstNameWeight` — вес имени (по умолчанию: 25)
- `lastNameWeight` — вес фамилии (по умолчанию: 20)
- `birthDateWeight` — вес даты рождения (по умолчанию: 15)
- `birthPlaceWeight` — вес места рождения (по умолчанию: 10)
- `deathDateWeight` — вес даты смерти (по умолчанию: 3)
- `genderWeight` — вес пола (по умолчанию: 2)
- `familyRelationsWeight` — вес семейных связей (по умолчанию: 25)
  - Учитываются родители, супруги, дети и братья/сестры
  - Повышает точность совпадения, но требует наличия семейных данных в Geni
  - **Важно:** этот параметр критичен для качественного матчинга, рекомендуется значение 20-25
- `matchThreshold` — минимальный порог совпадения 0-100 (по умолчанию: 70)
- `autoMatchThreshold` — порог автоматического совпадения (по умолчанию: 90)
- `maxBirthYearDifference` — максимальная разница в годах рождения (по умолчанию: 10)

**Примечание:** сумма весов должна составлять 100 для корректной работы алгоритма.

#### `sync` — настройки синхронизации
- `maxDepth` — максимальная глубина BFS (null = без ограничений)
- `dryRun` — режим предпросмотра без создания профилей (по умолчанию: true)

#### `nameVariants` — словари вариантов имён
- `givenNamesCsv` — путь к CSV с вариантами имён
- `surnamesCsv` — путь к CSV с вариантами фамилий

#### `paths` — пути к файлам
- `stateFile` — файл состояния для возобновления (по умолчанию: sync_state.json)
- `reportFile` — файл отчёта (по умолчанию: sync_report.json)

#### `logging` — настройки логирования
- `verbose` — подробное логирование (по умолчанию: false)

## Алгоритм matching

Веса полей (сумма = 100):

| Поле | Вес | Метод |
|------|-----|-------|
| Имя | 25 | Словарь эквивалентов → Варианты из Names (транслитерация) → Jaro-Winkler |
| Фамилия | 20 | Девичья фамилия → Варианты из Names → Транслитерация → Jaro-Winkler |
| Семейные связи | 25 | Сравнение родителей (40%), супругов (30%), детей (20%), братьев/сестёр (10%) |
| Дата рождения | 15 | Точная=100%, ±1год=80%, ±2года=60%, ±5лет=40% |
| Место рождения | 10 | Jaccard similarity по токенам |
| Дата смерти | 3 | Аналогично дате рождения (бонус при наличии) |
| Пол | 2 | Штраф за несовпадение |

### Приоритеты сравнения имён

1. **Точное совпадение** нормализованных имён → 100%
2. **Эквиваленты из словаря** (John=Иван) → 95%
3. **Варианты из поля Names** (Alexandr=Александр) → 90%
4. **Совпадение первых слов** (учёт отчеств) → 85-90%
5. **Jaro-Winkler similarity** → 0-100%

Поле `Names` от Geni API содержит многоязычные варианты имён (en-US, ru и т.д.) и критически важно для корректного матчинга транслитераций и разных написаний.

## Словари имён

Рекомендуется использовать CSV из [tfmorris/Names](https://github.com/tfmorris/Names):
- `givenname_similar_names.csv` — 70,000 имён
- `surname_similar_names.csv` — 200,000 фамилий

```bash
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --given-names-csv Data/givenname_similar_names.csv \
  --surnames-csv Data/surname_similar_names.csv \
  ...
```

## Структура проекта

```
GedcomGeniSync/
├── GedcomGeniSync.Core/         # Библиотека с логикой
│   ├── Models/                   # Модели данных
│   └── Services/                 # Сервисы (API, matching, sync)
├── GedcomGeniSync.Cli/           # Отдельный CLI-проект
│   └── Program.cs                # Консольные команды + DI/логирование
└── GedcomGeniSync.sln            # Solution файл
```

## Зависимости

- [GeneGenie.Gedcom](https://github.com/TheGeneGenieProject/GeneGenie.Gedcom) — парсинг GEDCOM
- [F23.StringSimilarity](https://github.com/feature23/StringSimilarity.NET) — Jaro-Winkler и другие алгоритмы (оптимизировано для славянских языков)
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — поддержка YAML конфигурации

## Лицензия

MIT
