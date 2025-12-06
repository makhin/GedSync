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
2. Получите access token через OAuth2

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
```

### 3. Анализ GEDCOM файла

```bash
dotnet run --project GedcomGeniSync.Cli -- analyze --gedcom family.ged --anchor @I123@
```

### 4. Тест matching логики

```bash
dotnet run --project GedcomGeniSync.Cli -- test-match
```

### 5. Синхронизация (dry-run)

```bash
# С использованием конфигурационного файла
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token YOUR_GENI_TOKEN

# Или с явным указанием параметров
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token YOUR_GENI_TOKEN \
  --threshold 70 \
  --verbose
```

### 6. Реальная синхронизация

```bash
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token YOUR_GENI_TOKEN \
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
- `firstNameWeight` — вес имени (по умолчанию: 30)
- `lastNameWeight` — вес фамилии (по умолчанию: 25)
- `birthDateWeight` — вес даты рождения (по умолчанию: 20)
- `birthPlaceWeight` — вес места рождения (по умолчанию: 15)
- `deathDateWeight` — вес даты смерти (по умолчанию: 5)
- `genderWeight` — вес пола (по умолчанию: 5)
- `matchThreshold` — минимальный порог совпадения 0-100 (по умолчанию: 70)
- `autoMatchThreshold` — порог автоматического совпадения (по умолчанию: 90)
- `maxBirthYearDifference` — максимальная разница в годах рождения (по умолчанию: 10)

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
| Имя | 30 | Словарь эквивалентов → Транслитерация → Levenshtein |
| Фамилия | 25 | Девичья фамилия → Транслитерация → Levenshtein |
| Дата рождения | 20 | Точная=100%, ±1год=80%, ±2года=60%, ±5лет=40% |
| Место рождения | 15 | Jaccard similarity по токенам |
| Дата смерти | 5 | Аналогично дате рождения |
| Пол | 5 | Штраф за несовпадение |

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
- [FuzzySharp](https://github.com/JakeBayer/FuzzySharp) — Levenshtein distance
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — поддержка YAML конфигурации

## Лицензия

MIT
