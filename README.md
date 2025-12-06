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

### 2. Анализ GEDCOM файла

```bash
dotnet run --project GedcomGeniSync.Cli -- analyze --gedcom family.ged --anchor @I123@
```

### 3. Тест matching логики

```bash
dotnet run --project GedcomGeniSync.Cli -- test-match
```

### 4. Синхронизация (dry-run)

```bash
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom family.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token YOUR_GENI_TOKEN \
  --threshold 70 \
  --verbose
```

### 5. Реальная синхронизация

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

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
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

## Лицензия

MIT
