# Release Notes: Update and Add Commands

## New Features

This release adds two new commands for synchronizing MyHeritage GEDCOM data to Geni.com based on `wave-compare` results.

### 1. `update` Command

Update existing Geni profiles with data from MyHeritage.

**Key Features:**
- ✅ Update all profile fields (names, dates, places, events)
- ✅ Upload photos from MyHeritage
- ✅ Dry-run mode for safe testing
- ✅ Selective field updates with `--skip-fields`
- ✅ Detailed error reporting and statistics

**Example:**
```bash
gedsync update --input comparison.json --gedcom myheritage.ged --dry-run
```

### 2. `add` Command

Add new profiles to Geni that exist in MyHeritage but not in Geni.

**Key Features:**
- ✅ Smart relationship detection (Parent/Child/Spouse)
- ✅ Depth-based filtering (`--max-depth`)
- ✅ Automatic profile tracking (for multi-level additions)
- ✅ Photo upload support
- ✅ Dry-run mode
- ✅ Processing by proximity (closest relatives first)

**Example:**
```bash
gedsync add --input comparison.json --gedcom myheritage.ged --max-depth 2
```

## Typical Workflow

```bash
# 1. Compare trees
gedsync wave-compare \
  --source myheritage.ged \
  --destination geni-export.ged \
  --anchor-source @I1@ \
  --anchor-destination @I100@ \
  --output comparison.json

# 2. Update existing profiles
gedsync update --input comparison.json --gedcom myheritage.ged

# 3. Add new profiles incrementally
gedsync add --input comparison.json --gedcom myheritage.ged --max-depth 1
gedsync add --input comparison.json --gedcom myheritage.ged --max-depth 2
```

## Implementation Details

### New Files

**Command Handlers:**
- `GedcomGeniSync.Cli/Commands/UpdateCommandHandler.cs` - CLI handler for update command
- `GedcomGeniSync.Cli/Commands/AddCommandHandler.cs` - CLI handler for add command

**Executors:**
- `GedcomGeniSync.Cli/Services/UpdateExecutor.cs` - Update logic and field mapping
- `GedcomGeniSync.Cli/Services/AddExecutor.cs` - Add logic and relationship handling

**Documentation:**
- `docs/UPDATE_AND_ADD_COMMANDS.md` - Comprehensive user guide

### Enhanced Models

**`GeniProfileUpdate` (GeniModels.cs):**
- Added `Names` (multilingual names support)
- Added `Nicknames`
- Added `Title`
- Added `IsAlive`
- Added `CauseOfDeath`
- Added `Baptism` event
- Added `Burial` event

**Event Models:**
- `GeniEventInput` - For creating/updating events
- `GeniDateInput` - Date components (year, month, day)
- `GeniLocationInput` - Location data

**Form Encoding:**
- Enhanced `CreateFormContent()` to support:
  - Multilingual names (`names[locale][field]`)
  - Event objects (`birth[date][year]`, etc.)
  - All new fields

### Field Mapping

**Update Command - Supported Fields:**
- Names: FirstName, MiddleName, LastName, MaidenName, Suffix, Nickname
- Dates: BirthDate, DeathDate, BurialDate
- Places: BirthPlace, DeathPlace, BurialPlace
- Other: Gender, Occupation
- Photos: PhotoUrl (MyHeritage)

**Date Parsing:**
- Supports various GEDCOM formats: "1950", "MAR 1950", "15 MAR 1950"
- Handles qualifiers: ABT, BEF, AFT, EST, CAL
- Extracts year, month (name or number), day

**Gender Mapping:**
- M → male
- F → female

### API Integration

**Update Command:**
- Uses `UpdateProfileAsync()` from GeniProfileClient
- Uses `SetMugshotFromBytesAsync()` for photos
- Uses `DownloadPhotoAsync()` from MyHeritagePhotoService

**Add Command:**
- Uses `AddChildAsync()` for Child relationships
- Uses `AddParentAsync()` for Parent relationships
- Uses `AddPartnerAsync()` for Spouse relationships
- Maintains profile map (SourceId → GeniProfileId) for multi-level additions

### Error Handling

Both commands:
- Continue processing on individual errors
- Collect detailed error information
- Provide comprehensive statistics
- Support verbose logging

### Safety Features

- **Dry-run mode**: Test without making changes
- **Field filtering**: Skip sensitive fields
- **Depth control**: Limit expansion in add command
- **Photo toggle**: Disable photo sync if needed

## Testing

The implementation has been tested with:
- Dry-run execution
- Field mapping validation
- Date parsing for various formats
- Relationship detection
- Error handling scenarios

Manual testing recommended for:
- Large batches (100+ profiles)
- Complex family structures
- Various GEDCOM formats
- Photo downloads

## Known Limitations

1. **Photos**: Only MyHeritage URLs supported
2. **Multilingual names**: Implementation ready, but requires GEDCOM with language tags
3. **Rate limiting**: Geni API limits apply (use incremental processing)
4. **Relationships**: Only Parent/Child/Spouse supported (Sibling requires union logic)

## Future Enhancements

Potential future additions:
- `fix-names` command for multilingual name normalization
- Resume capability for large batches
- Interactive mode with confirmations
- Progress bar for long-running operations
- Sibling relationship support (requires union API)

## Migration Notes

No breaking changes to existing functionality.

New commands are independent additions.

## Credits

Implementation based on:
- `.claude/UPDATE_ADD_COMMANDS_STRATEGY.md` - Design document
- Existing `wave-compare` infrastructure
- Existing Geni API client

## Version

This feature set is compatible with:
- .NET 8.0
- Geni API v1
- GEDCOM 5.5.1
