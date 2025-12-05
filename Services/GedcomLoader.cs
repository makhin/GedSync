using GedcomGeniSync.Models;
using GeneGenie.Gedcom;
using GeneGenie.Gedcom.Parser;
using GeneGenie.Gedcom.Enums;

namespace GedcomGeniSync.Services;

/// <summary>
/// Loads GEDCOM files and converts to PersonRecord models
/// </summary>
public class GedcomLoader
{
    private readonly ILogger _logger;
    
    public GedcomLoader(ILogger logger)
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
        
        using var gedcomReader = GedcomRecordReader.CreateReader(filePath);
        
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
            var person = ConvertIndividual(individual);
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

    private PersonRecord ConvertIndividual(GedcomIndividualRecord individual)
    {
        var person = new PersonRecord
        {
            Id = individual.XRefID,
            Source = PersonSource.Gedcom
        };

        // Names
        var primaryName = individual.Names.FirstOrDefault();
        if (primaryName != null)
        {
            person.FirstName = CleanName(primaryName.Given);
            person.LastName = CleanName(primaryName.Surname);
            person.MaidenName = CleanName(primaryName.SurnamePrefix); // Sometimes used for maiden name
            person.Nickname = CleanName(primaryName.Nick);
            person.Suffix = CleanName(primaryName.Suffix);
            
            // Store all name variants
            foreach (var name in individual.Names)
            {
                if (!string.IsNullOrEmpty(name.Given))
                    person.NameVariants.Add(name.Given);
                if (!string.IsNullOrEmpty(name.Surname))
                    person.NameVariants.Add(name.Surname);
            }
        }

        // Gender
        person.Gender = individual.Sex switch
        {
            GedcomSex.Male => Gender.Male,
            GedcomSex.Female => Gender.Female,
            _ => Gender.Unknown
        };

        // Events (Birth, Death, Burial)
        foreach (var evt in individual.Events)
        {
            switch (evt.EventType)
            {
                case GedcomEventType.Birth:
                    person.BirthDate = ConvertDate(evt.Date);
                    person.BirthPlace = GetPlace(evt.Place);
                    break;
                    
                case GedcomEventType.Death:
                    person.DeathDate = ConvertDate(evt.Date);
                    person.DeathPlace = GetPlace(evt.Place);
                    person.IsLiving = false;
                    break;
                    
                case GedcomEventType.Burial:
                    person.BurialDate = ConvertDate(evt.Date);
                    person.BurialPlace = GetPlace(evt.Place);
                    break;
            }
        }

        // Check if marked as living
        if (!person.IsLiving.HasValue)
        {
            // If no death date and birth year suggests they could be alive
            if (person.BirthYear.HasValue && person.BirthYear > DateTime.Now.Year - 120)
            {
                person.IsLiving = true;
            }
        }

        // Family links (raw IDs, will be resolved later)
        foreach (var familyLink in individual.ChildIn)
        {
            person.ChildOfFamilyIds.Add(familyLink.XRefID);
        }
        
        foreach (var familyLink in individual.SpouseIn)
        {
            person.SpouseOfFamilyIds.Add(familyLink.XRefID);
        }

        return person;
    }

    private void ProcessFamily(GedcomFamilyRecord family, Dictionary<string, PersonRecord> persons)
    {
        var husbandId = family.Husband?.XRefID;
        var wifeId = family.Wife?.XRefID;
        var childIds = family.Children.Select(c => c.XRefID).ToList();

        // Link children to parents
        foreach (var childId in childIds)
        {
            if (persons.TryGetValue(childId, out var child))
            {
                if (!string.IsNullOrEmpty(husbandId))
                {
                    child.FatherId = husbandId;
                }
                if (!string.IsNullOrEmpty(wifeId))
                {
                    child.MotherId = wifeId;
                }
            }
        }

        // Link spouses to each other
        if (!string.IsNullOrEmpty(husbandId) && persons.TryGetValue(husbandId, out var husband))
        {
            if (!string.IsNullOrEmpty(wifeId))
            {
                husband.SpouseIds.Add(wifeId);
            }
            husband.ChildrenIds.AddRange(childIds);
        }

        if (!string.IsNullOrEmpty(wifeId) && persons.TryGetValue(wifeId, out var wife))
        {
            if (!string.IsNullOrEmpty(husbandId))
            {
                wife.SpouseIds.Add(husbandId);
            }
            wife.ChildrenIds.AddRange(childIds);
            
            // Try to extract maiden name from family if not set
            if (string.IsNullOrEmpty(wife.MaidenName) && !string.IsNullOrEmpty(wife.LastName))
            {
                // Check if wife has different surname than husband
                if (husband != null && wife.LastName != husband.LastName)
                {
                    wife.MaidenName = wife.LastName;
                }
            }
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
                person.SiblingIds.AddRange(
                    siblings
                        .Where(s => s.Id != person.Id)
                        .Select(s => s.Id));
            }
        }
    }

    private static DateInfo? ConvertDate(GedcomDate? gedcomDate)
    {
        if (gedcomDate == null || string.IsNullOrEmpty(gedcomDate.Date1))
            return null;

        // GedcomDate has parsed components, use them
        var result = new DateInfo
        {
            Original = gedcomDate.Date1,
            Year = gedcomDate.DateTime1?.Year,
            Month = gedcomDate.DateTime1?.Month,
            Day = gedcomDate.DateTime1?.Day
        };

        // Handle date type/modifier
        // Note: GeneGenie.Gedcom may expose this differently
        // Fallback to parsing if needed
        if (!result.Year.HasValue)
        {
            return DateInfo.Parse(gedcomDate.Date1);
        }

        return result;
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

        // Remove GEDCOM-specific markers like slashes around surname
        return name
            .Replace("/", "")
            .Trim();
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
}

/// <summary>
/// Result of loading a GEDCOM file
/// </summary>
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
