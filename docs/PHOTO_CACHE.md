# Фото-кэш и сравнение

Документ описывает, как работает локальный кэш фото, где хранится индекс и как это используется при сравнении/загрузке.

## Назначение

- Скачивать фото из GEDCOM один раз и переиспользовать.
- Сравнивать фото по содержимому (SHA256 + perceptual hash).
- При upload на Geni использовать уже скачанные файлы.

## Структура кэша

```
{cacheDirectory}/
├── myheritage/
│   └── @I123@/
│       └── 500668_abc123.jpg
├── geni/
│   └── profile-6000000012345/
│       └── photo.jpg
├── other/
│   └── @I999@/
│       └── unknown_photo.jpg
└── cache.json
```

### cache.json

- `entries` — индекс по URL; каждая запись хранит путь, источник, размер, хеши и даты.
- `contentHash` — SHA256 (точное совпадение).
- `perceptualHash` — pHash для визуального сравнения.

## Конфигурация

Раздел `photo` в конфиге:

```yaml
photo:
  enabled: true
  cacheDirectory: "./photos"
  downloadOnLoad: true
  similarityThreshold: 0.95
  maxConcurrentDownloads: 4
```

## Поток данных

1. `GedcomLoader.LoadAsync(..., downloadPhotos: true)` собирает все URL фото и заполняет кэш.
2. `PhotoCompareService` сравнивает фото по SHA256, затем pHash.
3. `PersonFieldComparer` добавляет `FieldDiff` с:
   - `Action = AddPhoto` или `UpdatePhoto`
   - `LocalPhotoPath` для upload
   - `PhotoSimilarity` для похожих изображений
4. `AddExecutor` и `UpdateExecutor` сначала читают фото из кэша, и только потом скачивают.

## Замечания

- Если фото отсутствует на диске, сравнение/загрузка вернётся к скачиванию по URL.
- Перцептивное сравнение работает для jpg/png/webp и др. (через ImageSharp).
