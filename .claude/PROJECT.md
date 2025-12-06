# GedcomGeniSync Project Overview

## Project Purpose

GedcomGeniSync is a specialized synchronization tool designed to bridge genealogical data between GEDCOM files (used by MyHeritage, Ancestry, and other genealogy platforms) and Geni.com. The tool intelligently matches and creates family profiles while handling the complexities of multi-language names, transliteration, and fuzzy matching.

## Core Problem Statement

Genealogists often maintain family trees across multiple platforms. Manually synchronizing data between GEDCOM files and Geni.com is:
- Time-consuming and error-prone
- Difficult due to name variations (e.g., "Иван" vs "Ivan" vs "John")
- Complex when dealing with incomplete or approximate dates
- Challenging with Cyrillic/Latin transliteration

## Solution Approach

GedcomGeniSync automates this synchronization using:

1. **Intelligent Fuzzy Matching**: Sophisticated name and date comparison algorithms
2. **Breadth-First Synchronization**: Traverse family trees from an anchor person outward
3. **Multi-language Support**: Built-in Cyrillic/Latin transliteration and name equivalents
4. **Safe Operation**: Dry-run mode to preview changes before execution
5. **Resume Capability**: State persistence to continue after interruptions

## Key Features

### 1. Fuzzy Matching Engine
- **Name Variants**: Recognizes equivalent names across cultures (Иван = Ivan = John = Johann)
- **Transliteration**: Automatic Cyrillic ↔ Latin conversion
- **Maiden Names**: Intelligent matching using maiden names for married women
- **Flexible Dates**: Tolerance for date variations (±2 years, year-only, etc.)
- **Weighted Scoring**: Configurable weights for different matching criteria

### 2. BFS Tree Traversal
- Start from a verified anchor person (known match in both systems)
- Explore family relationships in layers: parents → spouses → children → siblings
- Configurable depth limits to control scope
- Automatic deduplication to avoid processing same person twice

### 3. Configuration System
- Support for both JSON and YAML configuration files
- CLI arguments override configuration file values
- Auto-discovery of configuration files in working directory
- Environment variable support for API tokens

### 4. Robust Error Handling
- Dry-run mode for safe testing
- State file persistence for resuming after failures
- Detailed reporting with match scores and actions taken
- Comprehensive logging with configurable verbosity

## Project Statistics

- **Primary Language**: C# (.NET 8)
- **Architecture**: Clean architecture with separated concerns
- **Code Coverage**: 80%+ unit test coverage
- **Key Dependencies**:
  - GeneGenie.Gedcom (GEDCOM parsing)
  - F23.StringSimilarity (Jaro-Winkler matching)
  - System.CommandLine (CLI interface)
  - YamlDotNet (YAML configuration)

## Project Structure

```
GedcomGeniSync/
├── GedcomGeniSync.Core/           # Core business logic library
│   ├── Models/                     # Domain models and DTOs
│   │   ├── PersonRecord.cs         # Unified person representation
│   │   ├── Configuration.cs        # Configuration models
│   │   └── DateInfo.cs             # Flexible date handling
│   ├── Services/                   # Business logic services
│   │   ├── FuzzyMatcherService.cs  # Matching algorithm
│   │   ├── SyncService.cs          # Synchronization orchestration
│   │   ├── GedcomLoader.cs         # GEDCOM file parsing
│   │   ├── GeniApiClient.cs        # Geni.com API client
│   │   └── ConfigurationLoader.cs  # Configuration management
│   └── Utils/                      # Utility classes
│       └── NameNormalizer.cs       # Name normalization utilities
├── GedcomGeniSync.Cli/             # CLI application
│   └── Program.cs                  # Command-line interface
├── GedcomGeniSync.Tests/           # Unit tests
└── GedcomGeniSync.sln              # Solution file
```

## Typical Usage Workflow

1. **Identify Anchor Person**: Find a person that exists in both GEDCOM and Geni
2. **Configure Settings**: Create a `gedsync.yaml` with matching thresholds and options
3. **Dry Run**: Execute sync in dry-run mode to preview matches
4. **Review Results**: Check the report to verify matching accuracy
5. **Execute Sync**: Run with dry-run disabled to create/link profiles
6. **Incremental Sync**: Use state file to resume or extend sync scope

## Example Use Case

A genealogist has a GEDCOM export from MyHeritage containing 500+ relatives and wants to sync to their Geni.com profile:

```bash
# Step 1: Analyze the GEDCOM file
dotnet run --project GedcomGeniSync.Cli -- analyze \
  --gedcom myheritage.ged \
  --anchor @I123@

# Step 2: Test sync in dry-run mode
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom myheritage.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token $GENI_TOKEN \
  --threshold 75 \
  --max-depth 5

# Step 3: Execute actual sync
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom myheritage.ged \
  --anchor-ged @I123@ \
  --anchor-geni 6000000012345678901 \
  --token $GENI_TOKEN \
  --dry-run false \
  --max-depth 5
```

## Success Metrics

The project is successful when:
- Matches with 85%+ accuracy for clear cases
- Safely skips ambiguous matches below threshold
- Handles Cyrillic and Latin names interchangeably
- Creates family structures preserving relationships
- Provides clear reporting for manual verification

## Current Status

- ✅ Core matching engine implemented and tested
- ✅ GEDCOM parsing with multi-language support
- ✅ Geni API client with all relationship types
- ✅ BFS synchronization algorithm
- ✅ Configuration system (JSON/YAML)
- ✅ State persistence for resume support
- ✅ 80%+ unit test coverage
- ✅ CLI interface with all commands
- 🔄 Production testing with real genealogy data (ongoing)
- 📋 Extended name variant dictionaries (future enhancement)

## License

MIT License - See LICENSE file for details
