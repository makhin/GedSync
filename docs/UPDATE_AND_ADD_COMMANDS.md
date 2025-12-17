# Update and Add Commands - User Guide

This guide explains how to use the `update` and `add` commands to synchronize data from MyHeritage GEDCOM to Geni.com based on `wave-compare` results.

## Overview

The synchronization workflow consists of three main steps:

1. **`wave-compare`** - Compare MyHeritage and Geni trees to identify differences
2. **`update`** - Update existing profiles in Geni with data from MyHeritage
3. **`add`** - Add new profiles to Geni that don't exist yet

## Workflow Example

### Step 1: Compare Trees

First, run `wave-compare` to analyze differences between your MyHeritage GEDCOM and Geni export:

```bash
gedsync wave-compare \
  --source myheritage.ged \
  --destination geni-export.ged \
  --anchor-source @I1@ \
  --anchor-destination @I100@ \
  --output comparison.json
```

This creates a JSON file with:
- `nodesToUpdate` - profiles that exist in both trees but have differences
- `nodesToAdd` - profiles from MyHeritage that don't exist in Geni

### Step 2: Update Existing Profiles

Review and apply updates to existing Geni profiles:

```bash
# First, run in dry-run mode to see what would be changed
gedsync update \
  --input comparison.json \
  --gedcom myheritage.ged \
  --dry-run \
  --verbose

# If everything looks good, apply the updates
gedsync update \
  --input comparison.json \
  --gedcom myheritage.ged
```

**Output example:**
```
=== Update Results ===
Total Processed: 150
Successful: 147
Failed: 3
Photos Uploaded: 45
Photos Failed: 2
```

### Step 3: Add New Profiles

Add new profiles from MyHeritage to Geni:

```bash
# Start with closest relatives (depth 1-2)
gedsync add \
  --input comparison.json \
  --gedcom myheritage.ged \
  --max-depth 2 \
  --dry-run \
  --verbose

# Apply the additions
gedsync add \
  --input comparison.json \
  --gedcom myheritage.ged \
  --max-depth 2
```

**Output example:**
```
=== Add Results ===
Total Processed: 75
Successful: 72
Failed: 1
Skipped (no relation): 2
Photos Uploaded: 20
Photos Failed: 0
```

## Command Reference

### `update` Command

Updates existing Geni profiles based on wave-compare results.

**Syntax:**
```bash
gedsync update --input <json> --gedcom <gedcom> [options]
```

**Required Parameters:**
- `--input` - JSON file from wave-compare
- `--gedcom` - MyHeritage GEDCOM file (source of full data)

**Optional Parameters:**
- `--token-file` - Geni API token file (default: `geni_token.json`)
- `--dry-run` - Simulate updates without making changes
- `--verbose` - Enable detailed logging
- `--sync-photos` - Upload photos from MyHeritage (default: `true`)
- `--skip-fields` - Comma-separated list of fields to skip
  - Example: `--skip-fields "BirthPlace,DeathPlace"`

**Supported Fields:**
- Names: `FirstName`, `MiddleName`, `LastName`, `MaidenName`, `Suffix`, `Nickname`
- Dates: `BirthDate`, `DeathDate`, `BurialDate`
- Places: `BirthPlace`, `DeathPlace`, `BurialPlace`
- Other: `Gender`, `Occupation`
- Photos: `PhotoUrl` (MyHeritage photos)

**Examples:**

Update all fields including photos:
```bash
gedsync update --input results.json --gedcom source.ged
```

Update without photos:
```bash
gedsync update \
  --input results.json \
  --gedcom source.ged \
  --sync-photos false
```

Update only names and dates, skip places:
```bash
gedsync update \
  --input results.json \
  --gedcom source.ged \
  --skip-fields "BirthPlace,DeathPlace,BurialPlace"
```

### `add` Command

Adds new profiles to Geni based on wave-compare results.

**Syntax:**
```bash
gedsync add --input <json> --gedcom <gedcom> [options]
```

**Required Parameters:**
- `--input` - JSON file from wave-compare
- `--gedcom` - MyHeritage GEDCOM file

**Optional Parameters:**
- `--token-file` - Geni API token file (default: `geni_token.json`)
- `--dry-run` - Simulate additions without making changes
- `--verbose` - Enable detailed logging
- `--sync-photos` - Upload photos from MyHeritage (default: `true`)
- `--max-depth` - Maximum depth from existing nodes to add
  - Depth 1 = direct relatives of existing profiles
  - Depth 2 = grandchildren, grandparents, siblings-in-law, etc.
  - Use this to control how far into the tree you want to expand

**How It Works:**

The `add` command:
1. Builds a map of existing profiles (SourceId → GeniProfileId)
2. Sorts nodes by depth (processes closest relatives first)
3. For each node:
   - Finds the related existing profile
   - Calls appropriate Geni API based on relationship:
     - `Parent` → AddParentAsync()
     - `Child` → AddChildAsync()
     - `Spouse` → AddPartnerAsync()
   - Adds new profile to the map for future references
   - Uploads photo if available

**Examples:**

Add only immediate family (parents, children, spouses):
```bash
gedsync add \
  --input results.json \
  --gedcom source.ged \
  --max-depth 1
```

Add up to second-degree relatives:
```bash
gedsync add \
  --input results.json \
  --gedcom source.ged \
  --max-depth 2
```

Add all new profiles (no depth limit):
```bash
gedsync add \
  --input results.json \
  --gedcom source.ged
```

## Best Practices

### 1. Always Use Dry-Run First

Before making any changes, run with `--dry-run` to preview:

```bash
gedsync update --input results.json --gedcom source.ged --dry-run --verbose
gedsync add --input results.json --gedcom source.ged --max-depth 1 --dry-run --verbose
```

### 2. Process Incrementally

For large family trees:

```bash
# First, update existing profiles
gedsync update --input results.json --gedcom source.ged

# Then add closest relatives
gedsync add --input results.json --gedcom source.ged --max-depth 1

# Then expand further
gedsync add --input results.json --gedcom source.ged --max-depth 2

# Continue as needed
gedsync add --input results.json --gedcom source.ged --max-depth 3
```

### 3. Handle Photos Separately

If you want to update data first, then add photos:

```bash
# Update data without photos
gedsync update \
  --input results.json \
  --gedcom source.ged \
  --sync-photos false

# Then update with photos only
gedsync update \
  --input results.json \
  --gedcom source.ged \
  --skip-fields "FirstName,LastName,BirthDate,BirthPlace,DeathDate,DeathPlace"
```

### 4. Skip Sensitive Fields

If you don't want to update certain fields:

```bash
gedsync update \
  --input results.json \
  --gedcom source.ged \
  --skip-fields "Occupation,BirthPlace,DeathPlace"
```

### 5. Monitor Errors

Both commands provide detailed error reporting:

```
Errors:
  [@I123] BirthPlace: Failed to update profile
  [@I456] PhotoUrl: Failed to download photo from MyHeritage
  [@I789] Child: API returned null profile
```

Review errors and retry if needed.

## Troubleshooting

### "No valid token found"

Run the `auth` command first:
```bash
gedsync auth
```

### "Related node not found in profile map"

This happens when trying to add a profile whose parent hasn't been added yet. Solutions:
- Process in smaller depth increments
- Check that `wave-compare` correctly identified anchor points
- Verify the relationship is valid in your GEDCOM

### "Failed to download photo from MyHeritage"

MyHeritage photos may require authentication. Try:
- Downloading photos manually
- Using `--sync-photos false` to skip photos
- Checking photo URLs in GEDCOM are valid

### "API returned null profile"

This can happen if:
- Geni API rate limiting is triggered (wait and retry)
- The relationship already exists
- Profile data is invalid

Use `--verbose` for detailed logging to diagnose the issue.

## API Rate Limiting

Geni API has rate limits. The tool automatically throttles requests, but for large batches:
- Process in smaller increments (use `--max-depth`)
- Monitor for "Too Many Requests" errors
- Wait 1-2 hours if you hit rate limits

## Data Mapping

### Date Formats

The tool parses various GEDCOM date formats:
- `1950` → Year only
- `MAR 1950` → Month and year
- `15 MAR 1950` → Full date
- `ABT 1950`, `BEF 1950`, `AFT 1950` → Qualifiers removed, year extracted

### Gender Mapping

- `M` → `male`
- `F` → `female`

### Photos

- Only MyHeritage URLs are supported
- Photos are uploaded as "mugshots" (primary profile photo)
- Maximum size: determined by Geni API limits

## Advanced Usage

### Combining with wave-compare filters

Use wave-compare filtering for better control:

```bash
# Only high-confidence matches (score >= 90)
gedsync wave-compare \
  --source source.ged \
  --destination dest.ged \
  --anchor-source @I1@ \
  --anchor-destination @I100@ \
  --threshold-strategy conservative \
  --output results.json

# Then update only high-confidence matches
gedsync update --input results.json --gedcom source.ged
```

### Processing subsets

Edit the JSON file to process only specific profiles:
1. Run wave-compare
2. Edit `comparison.json` to remove unwanted nodes
3. Run update/add with edited JSON

## See Also

- [Wave Compare Documentation](./wave-compare-json-format.md)
- [GEDCOM Export Guide](./geni-export.md)
- [Authentication Guide](./authentication.md)
