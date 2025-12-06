# GedcomGeniSync Development Guide

## Development Environment Setup

### Prerequisites

- **.NET 8 SDK** or higher
- **Git** for version control
- **IDE**: Visual Studio 2022, VS Code with C# extension, or JetBrains Rider
- **Optional**: Docker for containerized development

### Initial Setup

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/GedcomGeniSync.git
cd GedcomGeniSync

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the CLI
dotnet run --project GedcomGeniSync.Cli -- --help
```

### IDE Configuration

#### Visual Studio Code
Install extensions:
- C# (Microsoft)
- C# Dev Kit
- .NET Core Test Explorer

Recommended settings (`.vscode/settings.json`):
```json
{
  "omnisharp.enableRoslynAnalyzers": true,
  "omnisharp.enableEditorConfigSupport": true,
  "editor.formatOnSave": true
}
```

#### Visual Studio 2022
- Enable "Format on Save" in Tools → Options → Text Editor → C#
- Use Solution Explorer to navigate the project structure
- Use Test Explorer for running unit tests

## Project Structure Explained

```
GedcomGeniSync/
├── GedcomGeniSync.Core/              # Core library (reusable)
│   ├── Models/                        # Domain models
│   │   ├── PersonRecord.cs            # Main person representation
│   │   ├── Configuration.cs           # Configuration models
│   │   └── (other models)
│   ├── Services/                      # Business logic
│   │   ├── FuzzyMatcherService.cs     # Matching algorithm
│   │   ├── SyncService.cs             # Sync orchestration
│   │   ├── GedcomLoader.cs            # GEDCOM parsing
│   │   ├── GeniApiClient.cs           # Geni API client
│   │   ├── ConfigurationLoader.cs     # Config file handling
│   │   └── NameVariantsService.cs     # (embedded in FuzzyMatcherService.cs)
│   └── Utils/                         # Utility classes
│       └── NameNormalizer.cs          # Name normalization
├── GedcomGeniSync.Cli/                # CLI application
│   └── Program.cs                     # Entry point + DI setup
├── GedcomGeniSync.Tests/              # Unit tests
│   ├── FuzzyMatcherServiceTests.cs    # Matching tests
│   ├── NameNormalizerTests.cs         # Name normalization tests
│   ├── DateInfoTests.cs               # Date parsing tests
│   ├── PersonRecordTests.cs           # Model tests
│   └── NameVariantsServiceTests.cs    # Name variants tests
├── GedcomGeniSync.sln                 # Solution file
├── README.md                          # User documentation
├── gedsync.example.json               # Example JSON config
├── gedsync.example.yaml               # Example YAML config
└── .claude/                           # Claude Code documentation
    ├── PROJECT.md                     # Project overview
    ├── ARCHITECTURE.md                # Architecture details
    └── DEVELOPMENT.md                 # This file
```

## Key Files to Know

### Core Library Files

#### `PersonRecord.cs` (Models)
- **What**: Unified person representation for both GEDCOM and Geni
- **When to modify**: Adding new person attributes or relationship types
- **Key concerns**: Maintain immutability, update normalization logic if needed

#### `FuzzyMatcherService.cs` (Services)
- **What**: Core matching algorithm with configurable weights
- **When to modify**: Adjusting matching logic, adding new comparison fields
- **Key concerns**: Performance (called frequently), scoring accuracy

#### `SyncService.cs` (Services)
- **What**: BFS synchronization orchestration
- **When to modify**: Changing sync algorithm, adding new relationship types
- **Key concerns**: State management, error handling, API rate limits

#### `GedcomLoader.cs` (Services)
- **What**: Parses GEDCOM files into PersonRecords
- **When to modify**: Supporting new GEDCOM tags, fixing parsing issues
- **Key concerns**: GEDCOM standard compliance, memory usage

#### `GeniApiClient.cs` (Services)
- **What**: Geni.com API wrapper
- **When to modify**: Adding new API endpoints, changing error handling
- **Key concerns**: Dry-run mode correctness, API authentication

### CLI Files

#### `Program.cs` (Cli)
- **What**: Command-line interface using System.CommandLine
- **When to modify**: Adding new commands, changing CLI options
- **Key concerns**: Backward compatibility, help text clarity

## Development Workflow

### Making Changes

1. **Create a branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make changes** following coding standards (see below)

3. **Run tests**:
   ```bash
   dotnet test
   ```

4. **Check code coverage**:
   ```bash
   dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
   ```

5. **Build in release mode**:
   ```bash
   dotnet build -c Release
   ```

6. **Commit changes**:
   ```bash
   git add .
   git commit -m "feat: your descriptive commit message"
   ```

7. **Push and create PR**:
   ```bash
   git push origin feature/your-feature-name
   ```

### Commit Message Convention

Follow [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` New feature
- `fix:` Bug fix
- `docs:` Documentation changes
- `test:` Adding or updating tests
- `refactor:` Code refactoring
- `perf:` Performance improvements
- `chore:` Build/tooling changes

Examples:
```
feat: add support for Hebrew month names in GEDCOM dates
fix: handle null birth dates in fuzzy matcher
docs: update README with new configuration options
test: add test cases for maiden name matching
```

## Coding Standards

### C# Style Guide

Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions):

```csharp
// ✅ Good: Use PascalCase for public members
public class PersonRecord
{
    public string FirstName { get; init; }
    public string LastName { get; init; }

    // ✅ Use camelCase for private fields with _ prefix
    private readonly ILogger<PersonRecord> _logger;

    // ✅ Use meaningful names
    public int CalculateMatchScore(PersonRecord other)
    {
        // Implementation
    }

    // ❌ Avoid: Unclear abbreviations
    // public int CalcMtchScr(PersonRecord p)
}

// ✅ Use records for immutable data
public record MatchingOptions
{
    public int FirstNameWeight { get; init; } = 30;
    public int LastNameWeight { get; init; } = 25;
}

// ✅ Use nullable reference types
public string? GetOptionalValue()
{
    return null; // Explicitly nullable
}

// ✅ Add XML documentation for public APIs
/// <summary>
/// Compares two persons and returns a match score (0-100)
/// </summary>
/// <param name="source">The source person to compare</param>
/// <param name="target">The target person to compare</param>
/// <returns>Match score between 0 and 100</returns>
public MatchCandidate Compare(PersonRecord source, PersonRecord target)
{
    // Implementation
}
```

### Naming Conventions

- **Services**: Suffix with `Service` (e.g., `FuzzyMatcherService`)
- **Interfaces**: Prefix with `I` (e.g., `ILogger<T>`)
- **Test Classes**: Suffix with `Tests` (e.g., `FuzzyMatcherServiceTests`)
- **Test Methods**: Use descriptive names (e.g., `CompareNames_WithCyrillicAndLatin_ReturnsHighScore`)

### Immutability

Prefer immutable data structures:

```csharp
// ✅ Good: Use records with init-only properties
public record PersonRecord
{
    public required string Id { get; init; }
    public string? FirstName { get; init; }
}

// ✅ Good: Use ImmutableList for collections
public ImmutableList<string> SpouseIds { get; init; } = ImmutableList<string>.Empty;

// ❌ Avoid: Mutable classes (unless necessary)
public class PersonRecord
{
    public string Id { get; set; }
}
```

### Error Handling

```csharp
// ✅ Good: Use specific exceptions
if (string.IsNullOrEmpty(token))
{
    throw new ArgumentException("Geni access token is required", nameof(token));
}

// ✅ Good: Log errors with context
try
{
    await _geniClient.CreateProfileAsync(profile);
}
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "Failed to create profile for {Name}", profile.FullName);
    return null; // Graceful degradation
}

// ❌ Avoid: Swallowing exceptions
try
{
    await DoSomething();
}
catch
{
    // Silent failure - bad!
}
```

## Testing Guidelines

### Unit Test Structure

Use **Arrange-Act-Assert** pattern:

```csharp
[Fact]
public void CompareNames_WithEquivalentNames_ReturnsHighScore()
{
    // Arrange
    var matcher = new FuzzyMatcherService(
        new NameVariantsService(Mock.Of<ILogger<NameVariantsService>>()),
        Mock.Of<ILogger<FuzzyMatcherService>>());

    var person1 = new PersonRecord
    {
        Id = "1",
        Source = PersonSource.Gedcom,
        FirstName = "Иван",
        NormalizedFirstName = NameNormalizer.Normalize("Иван")
    };

    var person2 = new PersonRecord
    {
        Id = "2",
        Source = PersonSource.Geni,
        FirstName = "John",
        NormalizedFirstName = NameNormalizer.Normalize("John")
    };

    // Act
    var result = matcher.Compare(person1, person2);

    // Assert
    Assert.True(result.Score > 80,
        $"Expected high score for Иван=John, got {result.Score}");
}
```

### Test Naming

Use descriptive test names:

```csharp
// ✅ Good: Clear what is being tested
[Fact]
public void ParseDate_WithYearOnly_SetsPrecisionToYear()

[Fact]
public void ParseDate_WithRussianMonthName_ParsesCorrectly()

[Theory]
[InlineData("JAN 1885", 1885, 1)]
[InlineData("ЯНВ 1885", 1885, 1)]
public void ParseDate_WithVariousFormats_ParsesMonth(string input, int year, int month)

// ❌ Avoid: Unclear purpose
[Fact]
public void Test1()
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~FuzzyMatcherServiceTests"

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run tests in watch mode (re-run on file changes)
dotnet watch test --project GedcomGeniSync.Tests
```

### Coverage Goals

- **Minimum**: 70% overall coverage
- **Target**: 80%+ coverage
- **Critical paths**: 90%+ (matching algorithm, sync logic)
- **Excluded**: DTOs, configuration classes (marked with `[ExcludeFromCodeCoverage]`)

## Debugging Tips

### Debugging CLI Commands

```bash
# Run with verbose logging
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom test.ged \
  --anchor-ged @I1@ \
  --anchor-geni 6000000012345678901 \
  --token $GENI_TOKEN \
  --verbose

# Use dry-run to test without API calls
dotnet run --project GedcomGeniSync.Cli -- sync \
  --gedcom test.ged \
  --anchor-ged @I1@ \
  --anchor-geni 6000000012345678901 \
  --token $GENI_TOKEN \
  --dry-run true
```

### Debug in VS Code

Add to `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug Sync Command",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/GedcomGeniSync.Cli/bin/Debug/net8.0/GedcomGeniSync.Cli.dll",
      "args": [
        "sync",
        "--gedcom", "test.ged",
        "--anchor-ged", "@I1@",
        "--anchor-geni", "6000000012345678901",
        "--token", "${env:GENI_ACCESS_TOKEN}",
        "--dry-run", "true",
        "--verbose"
      ],
      "cwd": "${workspaceFolder}/GedcomGeniSync.Cli",
      "console": "internalConsole",
      "stopAtEntry": false
    }
  ]
}
```

### Logging Best Practices

```csharp
// ✅ Good: Structured logging
_logger.LogInformation(
    "Matched {GedcomId} to Geni profile {GeniId} with score {Score}%",
    gedcomId, geniId, score);

// ✅ Good: Use appropriate log levels
_logger.LogDebug("Processing person {Id}", person.Id); // Verbose details
_logger.LogInformation("Starting sync..."); // Key milestones
_logger.LogWarning("No match found for {Name}", person.FullName); // Potential issues
_logger.LogError(ex, "Failed to create profile"); // Errors

// ❌ Avoid: String concatenation
_logger.LogInformation("Matched " + gedcomId + " to " + geniId);
```

## Common Tasks

### Adding a New Matching Field

1. **Add property to PersonRecord**:
   ```csharp
   public string? Occupation { get; init; }
   ```

2. **Update MatchingOptions**:
   ```csharp
   public int OccupationWeight { get; init; } = 5;
   ```

3. **Add comparison method in FuzzyMatcherService**:
   ```csharp
   private double CompareOccupations(string? source, string? target)
   {
       if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
           return 0;

       return _jaroWinkler.Similarity(source, target);
   }
   ```

4. **Integrate in Compare method**:
   ```csharp
   var occupationScore = CompareOccupations(source.Occupation, target.Occupation);
   if (occupationScore > 0)
   {
       reasonsBuilder.Add(new MatchReason
       {
           Field = "Occupation",
           Points = occupationScore * _options.OccupationWeight,
           Details = $"{source.Occupation} ↔ {target.Occupation}"
       });
   }
   ```

5. **Add unit tests**:
   ```csharp
   [Fact]
   public void Compare_WithMatchingOccupation_IncreasesScore()
   {
       // Test implementation
   }
   ```

### Adding a New CLI Command

1. **Create command builder method**:
   ```csharp
   private static Command BuildMyCommand()
   {
       var myCommand = new Command("my-command", "Description");
       var option = new Option<string>("--option", "Option desc");
       myCommand.AddOption(option);

       myCommand.SetHandler(async context =>
       {
           var value = context.ParseResult.GetValueForOption(option);
           // Implementation
       });

       return myCommand;
   }
   ```

2. **Register in Main**:
   ```csharp
   var myCommand = BuildMyCommand();
   rootCommand.AddCommand(myCommand);
   ```

### Updating Configuration Schema

1. **Update Configuration.cs models**:
   ```csharp
   public record MyNewConfig
   {
       public string NewSetting { get; init; } = "default";
   }
   ```

2. **Update example configs**:
   - `gedsync.example.json`
   - `gedsync.example.yaml`

3. **Update ConfigurationLoader if needed**

4. **Update README.md documentation**

## Performance Profiling

### Using dotnet-trace

```bash
# Install dotnet-trace
dotnet tool install --global dotnet-trace

# Run with tracing
dotnet-trace collect --process-id $(pgrep -f GedcomGeniSync.Cli)

# Analyze trace in Visual Studio or PerfView
```

### Benchmarking

For performance-critical code, use BenchmarkDotNet:

```csharp
[MemoryDiagnoser]
public class MatchingBenchmarks
{
    [Benchmark]
    public void CompareNames()
    {
        // Benchmark code
    }
}
```

## Troubleshooting

### Common Issues

#### "Could not load file or assembly"
**Solution**: Run `dotnet restore` and `dotnet build`

#### Tests fail with NullReferenceException
**Solution**: Check that all required properties are initialized in test PersonRecords

#### GEDCOM parsing fails
**Solution**: Check GEDCOM file encoding (should be UTF-8) and format version

#### Geni API returns 401
**Solution**: Verify token is valid and has required permissions

#### Low matching scores
**Solution**: Review MatchingOptions weights, check name variant dictionaries are loaded

## Resources

### Documentation
- [.NET 8 Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [System.CommandLine Documentation](https://github.com/dotnet/command-line-api)
- [GEDCOM 5.5.1 Standard](https://www.gedcom.org/gedcom.html)
- [Geni API Documentation](https://www.geni.com/platform/developer/api_endpoints)

### Libraries Used
- [GeneGenie.Gedcom](https://github.com/TheGeneGenieProject/GeneGenie.Gedcom) - GEDCOM parser
- [F23.StringSimilarity](https://github.com/feature23/StringSimilarity.NET) - String matching
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) - YAML parsing

### External Resources
- [tfmorris/Names](https://github.com/tfmorris/Names) - Name variants database

## Getting Help

- **Issues**: Check GitHub Issues for known problems
- **Discussions**: Use GitHub Discussions for questions
- **Contributing**: See CONTRIBUTING.md (if exists)
- **Code Review**: All PRs require review before merging

## Continuous Integration

The project uses GitHub Actions for CI/CD:

- **Build**: On every push and PR
- **Test**: Run all unit tests
- **Coverage**: Report code coverage
- **Release**: Automatic versioning on main branch

See `.github/workflows/` for CI configuration (future addition).
