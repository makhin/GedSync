# DateInfo Parser - Examples and Usage

## Overview

The `DateInfo.Parse` method has been enhanced to support multi-language date parsing and GEDCOM date modifiers.

## Supported Features

### 1. GEDCOM Date Modifiers

The parser recognizes standard GEDCOM date modifiers:

- `ABT` (About) - approximate date
- `BEF` (Before) - date before specified
- `AFT` (After) - date after specified
- `EST` (Estimated) - estimated date
- `CAL` (Calculated) - calculated date
- `BET ... AND ...` (Between) - date range

### 2. Multi-Language Month Names

The parser supports month names in multiple languages:

#### English
- Abbreviations: JAN, FEB, MAR, APR, MAY, JUN, JUL, AUG, SEP, OCT, NOV, DEC
- Full names: JANUARY, FEBRUARY, MARCH, APRIL, JUNE, JULY, AUGUST, SEPTEMBER, OCTOBER, NOVEMBER, DECEMBER

#### Russian (Русский)
- Abbreviations: ЯНВ, ФЕВ, МАР, АПР, МАЙ, ИЮН, ИЮЛ, АВГ, СЕН, ОКТ, НОЯ, ДЕК
- Full names: ЯНВАРЬ, ФЕВРАЛЬ, МАРТ, АПРЕЛЬ, МАЙТ, ИЮНЬ, ИЮЛЬ, АВГУСТ, СЕНТЯБРЬ, ОКТЯБРЬ, НОЯБРЬ, ДЕКАБРЬ

#### German (Deutsch)
- Abbreviations: JAN, FEB, MÄR, APR, MAI, JUN, JUL, AUG, SEP, OKT, NOV, DEZ

#### French (Français)
- Abbreviations: JANV, FÉVR, MARS, AVR, JUIN, JUIL, AOÛT, SEPT, DÉC

### 3. Flexible Date Formats

The parser handles various date formats with automatic culture detection.

## Usage Examples

### Basic Parsing

```csharp
// Standard GEDCOM format (day month year)
var date1 = DateInfo.Parse("25 JAN 1975");
// Result: Date = 1975-01-25, Precision = Day

// Year only
var date2 = DateInfo.Parse("1945");
// Result: Date = 1945-01-01, Precision = Year

// Month and year
var date3 = DateInfo.Parse("MAR 1950");
// Result: Date = 1950-03-01, Precision = Month
```

### Modifiers

```csharp
// About
var date4 = DateInfo.Parse("ABT 1920");
// Result: Modifier = About, Date = 1920-01-01

// Before
var date5 = DateInfo.Parse("BEF 15 DEC 1900");
// Result: Modifier = Before, Date = 1900-12-15

// After
var date6 = DateInfo.Parse("AFT 1880");
// Result: Modifier = After, Date = 1880-01-01

// Between
var date7 = DateInfo.Parse("BET 1940 AND 1950");
// Result: Modifier = Between, Date = 1940-01-01, RangeEnd.Date = 1950-01-01
```

### Multi-Language Support

```csharp
// Russian
var date8 = DateInfo.Parse("5 ЯНВ 1917");
// Result: Date = 1917-01-05, Precision = Day

var date9 = DateInfo.Parse("ФЕВРАЛЬ 1945");
// Result: Date = 1945-02-01, Precision = Month

// German
var date10 = DateInfo.Parse("1 MAI 1970");
// Result: Date = 1970-05-01, Precision = Day

// French
var date11 = DateInfo.Parse("14 JUIL 1789");
// Result: Date = 1789-07-14, Precision = Day
```

### Combined Modifiers and Languages

```csharp
// Russian with modifier
var date12 = DateInfo.Parse("ABT 10 МАР 1905");
// Result: Modifier = About, Date = 1905-03-10

// German with modifier
var date13 = DateInfo.Parse("BEF 25 DEZ 1950");
// Result: Modifier = Before, Date = 1950-12-25
```

### Output Formats

```csharp
var date = DateInfo.Parse("25 JAN 1975");

// Geni API format
string geniFormat = date.ToGeniFormat();
// Result: "1975-01-25"

// String representation
string display = date.ToString();
// Result: "25/01/1975"

// With modifier
var approxDate = DateInfo.Parse("ABT 1945");
string displayWithModifier = approxDate.ToString();
// Result: "ABT 1945"
```

## Date Precision

The parser automatically determines date precision:

- **Year**: Only year is known (e.g., "1945")
- **Month**: Year and month are known (e.g., "MAR 1950")
- **Day**: Full date is known (e.g., "25 JAN 1975")

```csharp
var yearOnly = DateInfo.Parse("1945");
Console.WriteLine(yearOnly.Year);      // 1945
Console.WriteLine(yearOnly.Month);     // null
Console.WriteLine(yearOnly.Day);       // null
Console.WriteLine(yearOnly.Precision); // DatePrecision.Year

var fullDate = DateInfo.Parse("25 JAN 1975");
Console.WriteLine(fullDate.Year);      // 1975
Console.WriteLine(fullDate.Month);     // 1
Console.WriteLine(fullDate.Day);       // 25
Console.WriteLine(fullDate.Precision); // DatePrecision.Day
```

## Error Handling

The parser gracefully handles invalid dates:

```csharp
// Invalid date (e.g., Feb 31) falls back to month precision
var invalid = DateInfo.Parse("31 FEB 2000");
// Result: Date = 2000-02-01, Precision = Month

// Unparseable strings return null
var unparsed = DateInfo.Parse("not a date");
// Result: null

// Empty/null strings return null
var empty = DateInfo.Parse("");
// Result: null
```

## Implementation Notes

1. **Case-insensitive**: Month names are matched case-insensitively
2. **Culture fallback**: If dictionary lookup fails, the parser tries `DateTime.TryParse` with multiple cultures (en-US, ru-RU, de-DE, fr-FR)
3. **Original preservation**: The original date string is preserved in the `Original` property
4. **Thread-safe**: `DateInfo` is an immutable record type

## Future Enhancements

For more advanced date parsing scenarios, consider integrating Humanizer library features:
- Natural language dates ("3 days ago", "next month")
- Relative date parsing
- Additional culture support

The Humanizer.Core package is already included in the project dependencies and can be used for custom date parsing scenarios.
