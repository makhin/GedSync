# GedcomGeniSync Architecture

## Architectural Overview

GedcomGeniSync follows a clean architecture pattern with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────┐
│                   CLI Layer                             │
│              (GedcomGeniSync.Cli)                       │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │ sync command │ │analyze command│ │test command  │   │
│  └──────────────┘ └──────────────┘ └──────────────┘   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                Service Layer                            │
│              (GedcomGeniSync.Core)                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │ SyncService  │ │FuzzyMatcher  │ │ GeniApiClient│   │
│  └──────────────┘ └──────────────┘ └──────────────┘   │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │GedcomLoader  │ │NameVariants  │ │Configuration │   │
│  └──────────────┘ └──────────────┘ └──────────────┘   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                  Model Layer                            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │PersonRecord  │ │  DateInfo    │ │MatchCandidate│   │
│  └──────────────┘ └──────────────┘ └──────────────┘   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│              External Dependencies                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │ Geni API     │ │GEDCOM Parser │ │Jaro-Winkler  │   │
│  └──────────────┘ └──────────────┘ └──────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## Core Components

### 1. Models Layer (`GedcomGeniSync.Core/Models/`)

#### PersonRecord (`PersonRecord.cs`)
**Purpose**: Unified representation of a person from any source (GEDCOM or Geni)

**Key Design Decisions**:
- **Immutable Record**: Thread-safe and predictable state
- **Source-Agnostic**: Works with both GEDCOM and Geni data
- **Pre-Normalized Names**: Caches normalized names for performance
- **Relationship Tracking**: Maintains family connections via IDs

**Properties**:
```csharp
- Id: Source-specific identifier
- Source: PersonSource (Gedcom | Geni)
- FirstName, LastName, MaidenName: Name components
- NormalizedFirstName, NormalizedLastName: Pre-computed for matching
- BirthDate, DeathDate: Flexible date representations
- Gender: Male | Female | Unknown
- FatherId, MotherId, SpouseIds, ChildrenIds: Relationship IDs
```

**Why Immutable Records**:
- Thread safety for parallel processing
- Predictable state management
- Easy to reason about data flow
- Works well with functional programming patterns

#### DateInfo (`PersonRecord.cs:157-428`)
**Purpose**: Handle GEDCOM's flexible date formats

**Key Features**:
- Supports year-only, year+month, and full dates
- Date modifiers (About, Before, After, Estimated, Calculated)
- Date ranges (Between X and Y)
- Multi-language month names (English, Russian, German, French)
- Precision tracking (Year, Month, Day)

**Why Custom Date Type**:
- GEDCOM dates are often imprecise ("ABT 1885", "BEF MAR 1890")
- Standard DateTime doesn't handle partial dates
- Need to preserve precision for matching logic

#### Configuration (`Configuration.cs`)
**Purpose**: Type-safe configuration with defaults

**Sections**:
- `MatchingConfig`: Algorithm weights and thresholds
- `SyncConfig`: BFS depth limits, dry-run mode
- `PathsConfig`: State file, report file paths
- `NameVariantsConfig`: CSV file paths for name dictionaries
- `LoggingConfig`: Verbosity settings

### 2. Services Layer (`GedcomGeniSync.Core/Services/`)

#### FuzzyMatcherService (`FuzzyMatcherService.cs`)
**Purpose**: Core matching algorithm for comparing PersonRecords

**Matching Algorithm**:
```
Total Score = Σ(FieldScore × FieldWeight) × NormalizationFactor

Default Weights (sum to 100):
├─ FirstName:   30 points
├─ LastName:    25 points
├─ BirthDate:   20 points
├─ BirthPlace:  15 points
├─ DeathDate:    5 points
└─ Gender:       5 points
```

**Name Matching Hierarchy** (short-circuit on first match):
1. **Exact normalized match**: 100% score
2. **Name equivalents dictionary**: 95% score (e.g., Иван = John)
3. **All name variants**: 90% score (includes nicknames, middle names)
4. **Jaro-Winkler similarity**: 0-100% score (for transliteration)
5. **Substring match bonus**: For diminutives (Александр ⊃ Саша)

**Date Matching Scoring**:
- Exact year match: 90-100% (based on precision)
- ±1 year: 80%
- ±2 years: 60%
- ±5 years: 40%
- ±10 years: 20%

**Design Decisions**:
- Pre-filtering by gender and birth year reduces comparisons
- Name variants service handles transliteration
- Jaro-Winkler chosen for its strength with Slavic languages
- Configurable weights allow tuning for different use cases

#### NameVariantsService (`FuzzyMatcherService.cs:475-785`)
**Purpose**: Name equivalence and transliteration

**Features**:
1. **Built-in Slavic Name Equivalents**:
   - 75+ common name groups (Иван = Ivan = John = Johann...)
   - Male and female names
   - Diminutives (Александр = Саша, Мария = Маша)

2. **Cyrillic ↔ Latin Transliteration**:
   - Russian and Ukrainian alphabets
   - Character-by-character mapping
   - Handles digraphs (Ж → Zh, Щ → Shch)

3. **CSV Dictionary Loading**:
   - Support for external name variant databases
   - Compatible with tfmorris/Names repository (70k+ given names, 200k+ surnames)

**Why Separate Service**:
- Reusable across different matching contexts
- Can be extended with custom dictionaries
- Keeps fuzzy matcher focused on scoring logic

#### SyncService (`SyncService.cs`)
**Purpose**: Orchestrate the synchronization process

**Algorithm Flow**:
```
1. Load GEDCOM file
2. Verify anchor person exists in both systems
3. Initialize mapping with anchor (GEDCOM ID ↔ Geni ID)
4. BFS traversal:
   ┌─────────────────────────────────────┐
   │ Queue: [(anchorGED, anchorGeni, 0)] │
   └─────────────────────────────────────┘
         │
         ▼
   ┌─────────────────────────────────────┐
   │ Dequeue (currentGED, currentGeni, d)│
   └─────────────────────────────────────┘
         │
         ├──► Get Geni family of currentGeni
         │
         ├──► For each relative in GEDCOM:
         │    ├─ Already processed? → Skip
         │    ├─ Insufficient data? → Skip & record
         │    ├─ Find match in Geni family:
         │    │  ├─ Match found?
         │    │  │  ├─ Record mapping
         │    │  │  └─ Enqueue for BFS
         │    │  └─ No match?
         │    │     ├─ Create new profile (if not dry-run)
         │    │     └─ Enqueue for BFS
         │    └─ Save state every 50 results
         │
         └──► Continue until queue empty or max depth reached
```

**State Management**:
- `_gedcomToGeniMap`: Tracks all GEDCOM → Geni mappings
- `_processedGedcomIds`: Prevents duplicate processing
- `_results`: All actions taken (matched, created, skipped, errors)

**Design Decisions**:
- BFS ensures nearby relatives are processed first
- State persistence allows resuming after errors
- Dry-run mode uses the API client's dry-run flag
- Periodic state saves prevent data loss

#### GedcomLoader (`GedcomLoader.cs`)
**Purpose**: Parse GEDCOM files into PersonRecords

**Responsibilities**:
- Load GEDCOM using GeneGenie.Gedcom library
- Convert GEDCOM individuals to PersonRecords
- Extract family relationships (parents, spouses, children)
- Pre-normalize names for matching performance
- Provide BFS traversal method

**Design Decisions**:
- Pre-normalization of names improves matching speed
- Immutable collections for thread safety
- Eager loading of all persons for quick lookups

#### Photo Services (`Services/Photo/*`)
**Purpose**: Download, cache, hash, and compare photos

**Components**:
- `PhotoCacheService`: persistent cache on disk with `cache.json` index
- `PhotoHashService`: SHA256 + perceptual hash
- `PhotoCompareService`: compares source/destination photos and reports matches/similarity
- `PhotoSourceDetector`: source detection for folder organization

**Design Decisions**:
- Local cache avoids repeated downloads
- SHA256 used for exact matches before perceptual comparison
- Comparison results carry local paths for upload

#### GeniApiClient (`GeniApiClient.cs`)
**Purpose**: Interface with Geni.com REST API

**API Operations**:
```csharp
- GetProfileAsync(id): Fetch single profile
- GetImmediateFamilyAsync(id): Get parents, spouses, children
- AddParentAsync(childId, parent): Create parent relationship
- AddChildAsync(parentId, child): Create child relationship
- AddPartnerAsync(personId, partner): Create spouse relationship
```

**Features**:
- Dry-run mode: Log actions without making API calls
- OAuth token authentication
- HttpClientFactory for connection pooling
- Error handling and retry logic (future enhancement)

**Design Decisions**:
- Dry-run flag at client level prevents accidental writes
- Async/await throughout for scalability
- Separate methods for each relationship type (type safety)

### 3. CLI Layer (`GedcomGeniSync.Cli/`)

#### Program (`Program.cs`)
**Purpose**: User interface via System.CommandLine

**Commands**:
1. **sync**: Main synchronization command
2. **analyze**: Inspect GEDCOM without syncing
3. **test-match**: Run matching algorithm tests

**Configuration Priority** (highest to lowest):
```
CLI Arguments → Environment Variables → Config File → Defaults
```

**Dependency Injection Setup**:
- Uses Microsoft.Extensions.DependencyInjection
- Scoped service provider per command execution
- HttpClientFactory for GeniApiClient
- Logging configured based on --verbose flag

**Design Decisions**:
- Command pattern for clear separation of concerns
- Builder pattern for constructing DI container
- Async command handlers for I/O operations
- Detailed error messages guide user to resolution

## Data Flow Example

### Matching Flow
```
Input: PersonRecord (GEDCOM) + List<PersonRecord> (Geni candidates)
  │
  ├─► Pre-filter by gender and birth year
  │   └─► Reduces candidates by ~50%
  │
  ├─► For each candidate:
  │   ├─► Compare first names:
  │   │   ├─► Check normalized exact match
  │   │   ├─► Check name variants dictionary
  │   │   ├─► Calculate Jaro-Winkler similarity
  │   │   └─► Score × FirstNameWeight
  │   │
  │   ├─► Compare last names (similar flow)
  │   ├─► Compare birth dates
  │   ├─► Compare birth places
  │   ├─► Compare gender (penalty for mismatch)
  │   └─► Sum weighted scores
  │
  └─► Sort by score, return matches above threshold

Output: List<MatchCandidate> (sorted by score)
```

### Synchronization Flow
```
User Input: GEDCOM file + Anchor IDs + Token
  │
  ├─► Load GEDCOM → Map<string, PersonRecord>
  ├─► Fetch Geni anchor profile
  ├─► Initialize mappings with anchor
  │
  └─► BFS Loop:
      ├─► Dequeue person + depth
      ├─► Fetch Geni family (API call)
      ├─► For each GEDCOM relative:
      │   ├─► Convert Geni profiles → PersonRecords
      │   ├─► FuzzyMatcher.FindMatches()
      │   ├─► Match found?
      │   │   ├─► Yes: Record mapping, enqueue
      │   │   └─► No: Create profile (API call), enqueue
      │   └─► Log result
      ├─► Save state (every 50 operations)
      └─► Continue until queue empty

Output: SyncReport + State file + Report file
```

## Key Design Patterns

### 1. **Repository Pattern**
- `GedcomLoader`: Repository for GEDCOM data
- `GeniApiClient`: Repository for Geni data
- Both abstract data source details from business logic

### 2. **Strategy Pattern**
- `FuzzyMatcherService`: Encapsulates matching algorithm
- `MatchingOptions`: Allows different matching strategies via configuration

### 3. **Builder Pattern**
- Service provider construction in `Program.cs`
- Fluent configuration API

### 4. **Immutable Data Structures**
- All models are immutable records
- Thread-safe by design
- Predictable state transitions

### 5. **Dependency Injection**
- All services injected via constructor
- Testable and modular
- Follows SOLID principles

## Performance Considerations

### Optimization Strategies

1. **Pre-Normalization**:
   - Names normalized once during loading
   - Cached in `PersonRecord.NormalizedFirstName/LastName`
   - Avoids repeated normalization during matching

2. **Pre-Filtering**:
   - Gender mismatch: Skip immediately
   - Birth year difference > 10 years: Skip immediately
   - Reduces comparisons by ~80% in typical datasets

3. **Short-Circuit Evaluation**:
   - Name matching stops at first high-confidence match
   - Exact match → Skip Jaro-Winkler calculation

4. **Lazy Loading**:
   - Geni family data fetched only when needed
   - API calls minimized via BFS strategy

5. **State Persistence**:
   - Save every 50 operations
   - Balance between safety and I/O overhead

### Scalability

**Current Limits**:
- GEDCOM size: Tested up to 5,000 persons
- API rate limits: Respects Geni's throttling (future: exponential backoff)
- Memory: Entire GEDCOM loaded in memory (acceptable for genealogy use case)

**Future Enhancements**:
- Parallel matching (multiple candidate comparisons)
- Batch API operations
- Streaming GEDCOM parsing for very large files

## Thread Safety

- **Models**: All immutable records (thread-safe)
- **Services**: Stateless or using thread-safe collections
- **State Management**: Single-threaded BFS (no concurrency issues)
- **API Client**: HttpClient is thread-safe

## Error Handling Strategy

1. **Validation Errors**: Fail fast with clear messages
2. **API Errors**: Log and continue (mark as error in report)
3. **Data Errors**: Skip problematic records, log details
4. **Network Errors**: Retry logic (future enhancement)
5. **State Corruption**: Graceful degradation, start fresh if needed

## Testing Strategy

- **Unit Tests**: Cover models, matching algorithms, name normalization
- **Integration Tests**: Test GEDCOM loading, API client (mocked)
- **Test Coverage**: 80%+ achieved
- **Test Data**: Real GEDCOM samples with multi-language names

## Security Considerations

1. **API Token**: Stored in environment variable, never in code
2. **Dry-Run Default**: Prevents accidental data modification
3. **State Files**: May contain personal data, user responsible for security
4. **HTTPS Only**: All API communication encrypted
5. **No Credential Storage**: Token must be provided per execution

## Future Architecture Enhancements

1. **Plugin System**: Allow custom matching algorithms
2. **Multi-Source Support**: Sync from multiple GEDCOM files
3. **Conflict Resolution**: UI for ambiguous matches
4. **Undo Functionality**: Reverse sync operations
5. **Incremental Sync**: Only sync changes since last run
6. **Web UI**: Browser-based alternative to CLI
