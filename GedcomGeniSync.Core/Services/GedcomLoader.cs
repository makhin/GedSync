using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using GeneGenie.Gedcom;
using Microsoft.Extensions.Logging;
using GeneGenie.Gedcom.Parser;
using GeneGenie.Gedcom.Enums;

namespace GedcomGeniSync.Services;

/// <summary>
/// Loads GEDCOM files and converts to PersonRecord models
/// </summary>
[ExcludeFromCodeCoverage]
public class GedcomLoader
{
    private readonly ILogger<GedcomLoader> _logger;

    public GedcomLoader(ILogger<GedcomLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load GEDCOM file and return dictionary of PersonRecords keyed by GEDCOM ID
    /// </summary>
    public GedcomLoadResult Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"GEDCOM file not found: {filePath}");
        }

        _logger.LogInformation("Loading GEDCOM file: {Path}", filePath);

        var result = new GedcomLoadResult();

        var gedcomReader = GedcomRecordReader.CreateReader(filePath);

        if (gedcomReader.Parser.ErrorState != GedcomErrorState.NoError)
        {
            throw new InvalidOperationException(
                $"Failed to parse GEDCOM file: {gedcomReader.Parser.ErrorState}");
        }

        var db = gedcomReader.Database;

        _logger.LogInformation("Found {Individuals} individuals and {Families} families",
            db.Individuals.Count, db.Families.Count);

        // First pass: create all PersonRecords
        foreach (var individual in db.Individuals)
        {
            var person = ConvertIndividual(individual, db);
            result.Persons[person.Id] = person;
        }

        // Second pass: resolve family relationships
        foreach (var family in db.Families)
        {
            ProcessFamily(family, result.Persons);
            result.Families[family.XRefID] = family;
        }

        // Third pass: calculate siblings
        CalculateSiblings(result.Persons);

        _logger.LogInformation("Loaded {Count} persons", result.Persons.Count);

        return result;
    }

    private PersonRecord ConvertIndividual(GedcomIndividualRecord individual, GedcomDatabase db)
    {
        string? firstName = null;
        string? lastName = null;
        string? maidenName = null;
        string? nickname = null;
        string? suffix = null;
        var nameVariantsBuilder = ImmutableList.CreateBuilder<string>();

        // Names
        var primaryName = individual.Names.FirstOrDefault();
        if (primaryName != null)
        {
            firstName = CleanName(primaryName.Given);
            lastName = CleanName(primaryName.Surname);
            nickname = CleanName(primaryName.Nick);
            suffix = CleanName(primaryName.Suffix);
        }

        // Process all names to find maiden name and store variants
        // Check for proper TYPE tags (MAIDEN, MARRIED, BIRTH) according to GEDCOM standard
        foreach (var name in individual.Names)
        {
            var nameType = name.Type?.ToUpperInvariant().Trim();

            // Store name variants for fuzzy matching
            if (!string.IsNullOrEmpty(name.Given))
                nameVariantsBuilder.Add(name.Given);
            if (!string.IsNullOrEmpty(name.Surname))
                nameVariantsBuilder.Add(name.Surname);

            // Look for maiden name from TYPE=MAIDEN or TYPE=BIRTH
            // MAIDEN is the standard type, but BIRTH is also used to indicate birth name
            if (nameType == "MAIDEN" || nameType == "BIRTH")
            {
                var surname = CleanName(name.Surname);
                if (!string.IsNullOrEmpty(surname))
                {
                    maidenName = surname;
                    _logger.LogDebug("Found maiden name '{MaidenName}' from NAME with TYPE={Type} for {Id}",
                        maidenName, nameType, individual.XRefID);
                }
            }
            // If TYPE=MARRIED, update the current last name to married name
            else if (nameType == "MARRIED")
            {
                var surname = CleanName(name.Surname);
                if (!string.IsNullOrEmpty(surname))
                {
                    lastName = surname;
                    _logger.LogDebug("Found married name '{MarriedName}' from NAME with TYPE=MARRIED for {Id}",
                        lastName, individual.XRefID);
                }
            }
        }

        // Fallback: if no maiden name found via TYPE and SurnamePrefix exists on primary name
        // Note: SurnamePrefix is not the correct GEDCOM way to store maiden names,
        // but some genealogy software may use it this way
        if (string.IsNullOrEmpty(maidenName) && primaryName != null)
        {
            var surnamePrefix = CleanName(primaryName.SurnamePrefix);
            if (!string.IsNullOrEmpty(surnamePrefix))
            {
                maidenName = surnamePrefix;
                _logger.LogDebug("Using SurnamePrefix '{SurnamePrefix}' as fallback maiden name for {Id}",
                    maidenName, individual.XRefID);
            }
        }

        // Gender
        var gender = individual.Sex switch
        {
            GedcomSex.Male => Gender.Male,
            GedcomSex.Female => Gender.Female,
            _ => Gender.Unknown
        };

        // Events (Birth, Death, Burial)
        DateInfo? birthDate = null;
        string? birthPlace = null;
        DateInfo? deathDate = null;
        string? deathPlace = null;
        DateInfo? burialDate = null;
        string? burialPlace = null;
        bool? isLiving = null;

        foreach (var evt in individual.Events)
        {
            switch (evt.EventType)
            {
                case GedcomEventType.Birth:
                    birthDate = ConvertDate(evt.Date);
                    birthPlace = GetPlace(evt.Place);
                    break;

                case GedcomEventType.DEAT:
                    deathDate = ConvertDate(evt.Date);
                    deathPlace = GetPlace(evt.Place);
                    isLiving = false;
                    break;

                case GedcomEventType.BURI:
                    burialDate = ConvertDate(evt.Date);
                    burialPlace = GetPlace(evt.Place);
                    break;
            }
        }

        // Check if marked as living
        if (!isLiving.HasValue)
        {
            // If no death date and birth year suggests they could be alive
            var birthYear = birthDate?.Year;
            if (birthYear.HasValue && birthYear > DateTime.Now.Year - 120)
            {
                isLiving = true;
            }
        }

        // Family links (raw IDs, will be resolved later)
        var childOfFamilyIdsBuilder = ImmutableList.CreateBuilder<string>();
        var spouseOfFamilyIdsBuilder = ImmutableList.CreateBuilder<string>();

        foreach (var familyLink in individual.ChildIn)
        {
            childOfFamilyIdsBuilder.Add(familyLink.XRefID);
        }

        foreach (var familyLink in individual.SpouseIn)
        {
            spouseOfFamilyIdsBuilder.Add(familyLink.XRefID);
        }

        // Extract photo URLs from multimedia records
        var photoUrlsBuilder = ImmutableList.CreateBuilder<string>();
        if (db.Media != null)
        {
            foreach (var multimediaLink in individual.Multimedia)
            {
                // Find the multimedia record in the database
                var multimediaRecord = db.Media.FirstOrDefault(m => m.XRefID == multimediaLink);
                if (multimediaRecord != null)
                {
                    var photoUrl = ExtractPhotoUrl(multimediaRecord);
                    if (!string.IsNullOrEmpty(photoUrl))
                    {
                        photoUrlsBuilder.Add(photoUrl);
                    }
                }
            }
        }

        return new PersonRecord
        {
            Id = individual.XRefID,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName,
            MaidenName = maidenName,
            Nickname = nickname,
            Suffix = suffix,
            NameVariants = nameVariantsBuilder.ToImmutable(),
            NormalizedFirstName = NameNormalizer.Normalize(firstName),
            NormalizedLastName = NameNormalizer.Normalize(lastName),
            Gender = gender,
            BirthDate = birthDate,
            BirthPlace = birthPlace,
            DeathDate = deathDate,
            DeathPlace = deathPlace,
            BurialDate = burialDate,
            BurialPlace = burialPlace,
            IsLiving = isLiving,
            ChildOfFamilyIds = childOfFamilyIdsBuilder.ToImmutable(),
            SpouseOfFamilyIds = spouseOfFamilyIdsBuilder.ToImmutable(),
            PhotoUrls = photoUrlsBuilder.ToImmutable()
        };
    }

    private string? ExtractPhotoUrl(GedcomMultimediaRecord multimedia)
    {
        if (multimedia == null)
            return null;

        // Try to get file reference from multimedia record
        var file = multimedia.Files?.FirstOrDefault();
        if (file != null && !string.IsNullOrWhiteSpace(file.Filename))
        {
            var filename = file.Filename.Trim();

            // Check if it's a URL (http/https)
            if (filename.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                filename.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return filename;
            }

            // Check if it's a local file path that we should keep
            // Some GEDCOM files store relative or absolute paths
            return filename;
        }

        return null;
    }

    private void ProcessFamily(GedcomFamilyRecord family, Dictionary<string, PersonRecord> persons)
    {
        var husbandId = ResolveXRefId(family.Husband);
        var wifeId = ResolveXRefId(family.Wife);
        var childIds = family.Children
            .Select(ResolveXRefId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();

        // Link children to parents
        foreach (var childId in childIds)
        {
            if (persons.TryGetValue(childId, out var child))
            {
                var updatedChild = child;

                if (!string.IsNullOrEmpty(husbandId))
                {
                    updatedChild = updatedChild with { FatherId = husbandId };
                }
                if (!string.IsNullOrEmpty(wifeId))
                {
                    updatedChild = updatedChild with { MotherId = wifeId };
                }

                persons[childId] = updatedChild;
            }
        }

        // Link spouses to each other
        PersonRecord? husband = null;
        if (!string.IsNullOrEmpty(husbandId) && persons.TryGetValue(husbandId, out var resolvedHusband))
        {
            husband = resolvedHusband;
            var updatedHusband = husband;

            if (!string.IsNullOrEmpty(wifeId))
            {
                updatedHusband = updatedHusband with
                {
                    SpouseIds = updatedHusband.SpouseIds.Add(wifeId)
                };
            }

            updatedHusband = updatedHusband with
            {
                ChildrenIds = updatedHusband.ChildrenIds.AddRange(childIds)
            };

            persons[husbandId] = updatedHusband;
            husband = updatedHusband; // Update reference for maiden name logic
        }

        if (!string.IsNullOrEmpty(wifeId) && persons.TryGetValue(wifeId, out var wife))
        {
            var updatedWife = wife;

            if (!string.IsNullOrEmpty(husbandId))
            {
                updatedWife = updatedWife with
                {
                    SpouseIds = updatedWife.SpouseIds.Add(husbandId)
                };
            }

            updatedWife = updatedWife with
            {
                ChildrenIds = updatedWife.ChildrenIds.AddRange(childIds)
            };

            // Try to extract maiden name from family if not set
            if (string.IsNullOrEmpty(updatedWife.MaidenName) && !string.IsNullOrEmpty(updatedWife.LastName))
            {
                // Check if wife has different surname than husband
                if (husband != null && updatedWife.LastName != husband.LastName)
                {
                    updatedWife = updatedWife with { MaidenName = updatedWife.LastName };
                }
            }

            persons[wifeId] = updatedWife;
        }
    }

    private void CalculateSiblings(Dictionary<string, PersonRecord> persons)
    {
        // Group by parents
        var siblingGroups = persons.Values
            .Where(p => !string.IsNullOrEmpty(p.FatherId) || !string.IsNullOrEmpty(p.MotherId))
            .GroupBy(p => (p.FatherId ?? "", p.MotherId ?? ""))
            .Where(g => g.Count() > 1);

        foreach (var group in siblingGroups)
        {
            var siblings = group.ToList();
            foreach (var person in siblings)
            {
                var siblingIds = siblings
                    .Where(s => s.Id != person.Id)
                    .Select(s => s.Id)
                    .ToList();

                var updatedPerson = person with
                {
                    SiblingIds = person.SiblingIds.AddRange(siblingIds)
                };

                persons[person.Id] = updatedPerson;
            }
        }
    }

    private static DateInfo? ConvertDate(GedcomDate? gedcomDate)
    {
        if (gedcomDate == null || string.IsNullOrEmpty(gedcomDate.Date1))
            return null;

        // GedcomDate has parsed components, use them
        var year = gedcomDate.DateTime1?.Year;
        var month = gedcomDate.DateTime1?.Month;
        var day = gedcomDate.DateTime1?.Day;

        // Handle date type/modifier
        // Note: GeneGenie.Gedcom may expose this differently
        // Fallback to parsing if needed
        if (!year.HasValue)
        {
            return DateInfo.Parse(gedcomDate.Date1);
        }

        // Determine precision and create DateOnly
        DatePrecision precision;
        DateOnly date;

        if (day.HasValue && month.HasValue)
        {
            precision = DatePrecision.Day;
            try
            {
                date = new DateOnly(year.Value, month.Value, day.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date (e.g., Feb 31), fall back to month precision
                precision = DatePrecision.Month;
                date = new DateOnly(year.Value, month.Value, 1);
            }
        }
        else if (month.HasValue)
        {
            precision = DatePrecision.Month;
            date = new DateOnly(year.Value, month.Value, 1);
        }
        else
        {
            precision = DatePrecision.Year;
            date = new DateOnly(year.Value, 1, 1);
        }

        return new DateInfo
        {
            Original = gedcomDate.Date1,
            Date = date,
            Precision = precision
        };
    }

    private static string? GetPlace(GedcomPlace? place)
    {
        if (place == null)
            return null;

        // GedcomPlace.Name typically contains the full place hierarchy
        return place.Name;
    }

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Remove GEDCOM-specific markers and special characters
        // Keep only letters (Latin and Cyrillic), digits, spaces, and hyphens
        name = Regex.Replace(name, @"[^A-Za-zА-Яа-яЁё0-9 -]", "");

        // Normalize multiple spaces to single space
        name = Regex.Replace(name, @"\s+", " ");

        return name.Trim();
    }

    /// <summary>
    /// Find person by GEDCOM ID
    /// </summary>
    public PersonRecord? FindById(GedcomLoadResult result, string gedcomId)
    {
        result.Persons.TryGetValue(gedcomId, out var person);
        return person;
    }

    /// <summary>
    /// Get all relatives of a person (for BFS traversal)
    /// </summary>
    public IEnumerable<string> GetRelativeIds(PersonRecord person)
    {
        var relatives = new List<string>();

        if (!string.IsNullOrEmpty(person.FatherId))
            relatives.Add(person.FatherId);

        if (!string.IsNullOrEmpty(person.MotherId))
            relatives.Add(person.MotherId);

        relatives.AddRange(person.SpouseIds);
        relatives.AddRange(person.ChildrenIds);
        relatives.AddRange(person.SiblingIds);

        return relatives.Distinct();
    }

    private static string? ResolveXRefId(object? link)
    {
        if (link == null)
        {
            return null;
        }

        if (link is string id)
        {
            return id;
        }

        var xRefIdProperty = link.GetType().GetProperty("XRefID");
        if (xRefIdProperty?.GetValue(link) is string xRefId)
        {
            return xRefId;
        }

        return null;
    }
}

/// <summary>
/// Result of loading a GEDCOM file
/// </summary>
[ExcludeFromCodeCoverage]
public class GedcomLoadResult
{
    /// <summary>
    /// All persons keyed by GEDCOM ID (e.g., "@I123@")
    /// </summary>
    public Dictionary<string, PersonRecord> Persons { get; } = new();

    /// <summary>
    /// Original family records for reference
    /// </summary>
    public Dictionary<string, GedcomFamilyRecord> Families { get; } = new();

    /// <summary>
    /// Statistics
    /// </summary>
    public int TotalPersons => Persons.Count;
    public int TotalFamilies => Families.Count;
    public int PersonsWithBirthDate => Persons.Values.Count(p => p.BirthDate != null);
    public int PersonsWithBirthPlace => Persons.Values.Count(p => !string.IsNullOrEmpty(p.BirthPlace));

    public void PrintStats(ILogger logger)
    {
        logger.LogInformation("=== GEDCOM Statistics ===");
        logger.LogInformation("Total persons: {Count}", TotalPersons);
        logger.LogInformation("Total families: {Count}", TotalFamilies);
        logger.LogInformation("Persons with birth date: {Count} ({Percent:P0})",
            PersonsWithBirthDate, (double)PersonsWithBirthDate / TotalPersons);
        logger.LogInformation("Persons with birth place: {Count} ({Percent:P0})",
            PersonsWithBirthPlace, (double)PersonsWithBirthPlace / TotalPersons);

        // Gender distribution
        var males = Persons.Values.Count(p => p.Gender == Gender.Male);
        var females = Persons.Values.Count(p => p.Gender == Gender.Female);
        logger.LogInformation("Males: {Males}, Females: {Females}, Unknown: {Unknown}",
            males, females, TotalPersons - males - females);
    }
}

/// <summary>
/// Extension methods for easier traversal
/// </summary>
[ExcludeFromCodeCoverage]
public static class GedcomLoadResultExtensions
{
    /// <summary>
    /// Get father of a person
    /// </summary>
    public static PersonRecord? GetFather(this GedcomLoadResult result, PersonRecord person)
    {
        if (string.IsNullOrEmpty(person.FatherId))
            return null;

        result.Persons.TryGetValue(person.FatherId, out var father);
        return father;
    }

    /// <summary>
    /// Get mother of a person
    /// </summary>
    public static PersonRecord? GetMother(this GedcomLoadResult result, PersonRecord person)
    {
        if (string.IsNullOrEmpty(person.MotherId))
            return null;

        result.Persons.TryGetValue(person.MotherId, out var mother);
        return mother;
    }

    /// <summary>
    /// Get all spouses of a person
    /// </summary>
    public static IEnumerable<PersonRecord> GetSpouses(this GedcomLoadResult result, PersonRecord person)
    {
        foreach (var spouseId in person.SpouseIds)
        {
            if (result.Persons.TryGetValue(spouseId, out var spouse))
                yield return spouse;
        }
    }

    /// <summary>
    /// Get all children of a person
    /// </summary>
    public static IEnumerable<PersonRecord> GetChildren(this GedcomLoadResult result, PersonRecord person)
    {
        foreach (var childId in person.ChildrenIds)
        {
            if (result.Persons.TryGetValue(childId, out var child))
                yield return child;
        }
    }

    /// <summary>
    /// Get all siblings of a person
    /// </summary>
    public static IEnumerable<PersonRecord> GetSiblings(this GedcomLoadResult result, PersonRecord person)
    {
        foreach (var siblingId in person.SiblingIds)
        {
            if (result.Persons.TryGetValue(siblingId, out var sibling))
                yield return sibling;
        }
    }

    /// <summary>
    /// BFS traversal from anchor person
    /// </summary>
    public static IEnumerable<PersonRecord> TraverseBfs(
        this GedcomLoadResult result,
        string anchorId,
        int? maxDepth = null)
    {
        if (!result.Persons.TryGetValue(anchorId, out var anchor))
            yield break;

        var visited = new HashSet<string>();
        var queue = new Queue<(PersonRecord Person, int Depth)>();

        queue.Enqueue((anchor, 0));
        visited.Add(anchorId);

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            yield return current;

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            // Add all relatives
            var relativeIds = new List<string>();

            if (!string.IsNullOrEmpty(current.FatherId))
                relativeIds.Add(current.FatherId);
            if (!string.IsNullOrEmpty(current.MotherId))
                relativeIds.Add(current.MotherId);

            relativeIds.AddRange(current.SpouseIds);
            relativeIds.AddRange(current.ChildrenIds);

            foreach (var relativeId in relativeIds)
            {
                if (visited.Contains(relativeId))
                    continue;

                if (result.Persons.TryGetValue(relativeId, out var relative))
                {
                    visited.Add(relativeId);
                    queue.Enqueue((relative, depth + 1));
                }
            }
        }
    }
}
