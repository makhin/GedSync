using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;
using Patagames.GedcomNetSdk;
using Patagames.GedcomNetSdk.Records;
using Patagames.GedcomNetSdk.Structures;
using Patagames.GedcomNetSdk.Dates;

namespace GedcomGeniSync.Services;

/// <summary>
/// Loads GEDCOM files and converts to PersonRecord models
/// Uses Gedcom.Net.SDK library for parsing GEDCOM 5.5, 5.5.1, 5.5.5 and 7.0
/// </summary>
[ExcludeFromCodeCoverage]
public class GedcomLoader : IGedcomLoader
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

        // Activate SDK for personal use (required for Gedcom.Net.SDK)
        try
        {
            Activate.ForPersonalUse("gedcom@gedsync.local");
        }
        catch (Exception)
        {
            // Already activated or activation not required
        }

        using var parser = new Parser(filePath);
        var transmission = new GedcomTransmission();
        transmission.Deserialize(parser);

        // Extract individuals and families from records
        var individuals = new List<IndividualRecord>();
        var families = new List<FamilyRecord>();
        var multimedia = new Dictionary<string, MultimediaRecord>();

        foreach (var record in transmission.Records)
        {
            if (record is IndividualRecord individual)
            {
                individuals.Add(individual);
            }
            else if (record is FamilyRecord family)
            {
                families.Add(family);
            }
            else if (record is MultimediaRecord media)
            {
                multimedia[media.MultimediaId] = media;
            }
        }

        _logger.LogInformation("Found {Individuals} individuals and {Families} families",
            individuals.Count, families.Count);

        // First pass: create all PersonRecords
        foreach (var individual in individuals)
        {
            var person = ConvertIndividual(individual, multimedia);
            result.Persons[person.Id] = person;

            // Build RIN mapping for ID resolution
            // AutomatedRecordId (RIN field in GEDCOM) often contains the original ID like "MH:I500002"
            var rin = GetAutomatedRecordId(individual);
            if (!string.IsNullOrEmpty(rin))
            {
                // Extract just the ID part after the colon (e.g., "MH:I500002" -> "I500002")
                var idPart = rin.Contains(':') ? rin.Split(':').Last() : rin;
                result.RinToXRefMapping[idPart] = person.Id;

                // Also map without @ symbols for flexibility
                var normalizedId = idPart.Trim('@');
                if (normalizedId != idPart)
                {
                    result.RinToXRefMapping[normalizedId] = person.Id;
                }

                _logger.LogDebug("RIN mapping: {RIN} -> {XRef}", rin, person.Id);
            }
        }

        // Second pass: resolve family relationships
        foreach (var family in families)
        {
            ProcessFamily(family, result.Persons);
            result.Families[family.FamilyId] = family;
        }

        // Third pass: calculate siblings
        CalculateSiblings(result.Persons);

        _logger.LogInformation("Loaded {Count} persons", result.Persons.Count);

        return result;
    }

    private PersonRecord ConvertIndividual(IndividualRecord individual, Dictionary<string, MultimediaRecord> multimedia)
    {
        string? firstName = null;
        string? lastName = null;
        string? maidenName = null;
        string? nickname = null;
        string? suffix = null;
        var nameVariantsBuilder = ImmutableList.CreateBuilder<string>();

        // Names - use the Name collection from IndividualRecord
        var names = individual.Name;
        var primaryName = names?.FirstOrDefault();
        if (primaryName != null)
        {
            // Try to extract name pieces
            var pieces = GetPersonalNamePieces(primaryName);
            if (pieces != null)
            {
                firstName = CleanName(pieces.GivenName);
                lastName = CleanName(pieces.Surname);
                nickname = CleanName(pieces.Nickname);
                suffix = CleanName(pieces.NameSuffix);
            }
            else
            {
                // Fall back to parsing the full name string
                ParseFullName(primaryName.Name, out firstName, out lastName);
            }
        }

        // Process all names to find maiden name and store variants
        if (names != null)
        {
            foreach (var name in names)
            {
                var pieces = GetPersonalNamePieces(name);
                var nameType = GetNameType(name)?.ToUpperInvariant().Trim();

                // Store name variants for fuzzy matching
                if (pieces != null)
                {
                    if (!string.IsNullOrEmpty(pieces.GivenName))
                        nameVariantsBuilder.Add(pieces.GivenName);
                    if (!string.IsNullOrEmpty(pieces.Surname))
                        nameVariantsBuilder.Add(pieces.Surname);
                }

                // Look for maiden name from TYPE=MAIDEN or TYPE=BIRTH
                if (nameType == "MAIDEN" || nameType == "BIRTH")
                {
                    var surname = pieces != null ? CleanName(pieces.Surname) : null;
                    if (!string.IsNullOrEmpty(surname))
                    {
                        maidenName = surname;
                        _logger.LogDebug("Found maiden name '{MaidenName}' from NAME with TYPE={Type} for {Id}",
                            maidenName, nameType, individual.IndividualId);
                    }
                }
                // If TYPE=MARRIED, update the current last name to married name
                else if (nameType == "MARRIED")
                {
                    var surname = pieces != null ? CleanName(pieces.Surname) : null;
                    if (!string.IsNullOrEmpty(surname))
                    {
                        lastName = surname;
                        _logger.LogDebug("Found married name '{MarriedName}' from NAME with TYPE=MARRIED for {Id}",
                            lastName, individual.IndividualId);
                    }
                }
            }
        }

        // Fallback: if no maiden name found via TYPE and SurnamePrefix exists on primary name
        if (string.IsNullOrEmpty(maidenName) && primaryName != null)
        {
            var pieces = GetPersonalNamePieces(primaryName);
            if (pieces != null)
            {
                var surnamePrefix = CleanName(pieces.SurnamePrefix);
                if (!string.IsNullOrEmpty(surnamePrefix))
                {
                    maidenName = surnamePrefix;
                    _logger.LogDebug("Using SurnamePrefix '{SurnamePrefix}' as fallback maiden name for {Id}",
                        maidenName, individual.IndividualId);
                }
            }
        }

        // Gender
        var gender = individual.Sex?.ToUpperInvariant() switch
        {
            "M" => Gender.Male,
            "F" => Gender.Female,
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

        // Get events from version-specific Individual classes
        var events = GetIndividualEvents(individual);
        if (events != null)
        {
            foreach (var evt in events)
            {
                var eventType = GetEventType(evt);
                var eventDetail = GetEventDetail(evt);

                if (eventDetail == null)
                    continue;

                switch (eventType)
                {
                    case "BIRT":
                        birthDate = ConvertDate(eventDetail.Date);
                        birthPlace = GetPlace(eventDetail.Place);
                        break;

                    case "DEAT":
                        deathDate = ConvertDate(eventDetail.Date);
                        deathPlace = GetPlace(eventDetail.Place);
                        isLiving = false;
                        break;

                    case "BURI":
                        burialDate = ConvertDate(eventDetail.Date);
                        burialPlace = GetPlace(eventDetail.Place);
                        break;
                }
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

        if (individual.ChildToFamilyLinks != null)
        {
            foreach (var familyLink in individual.ChildToFamilyLinks)
            {
                if (!string.IsNullOrEmpty(familyLink.FamilyId))
                {
                    childOfFamilyIdsBuilder.Add(familyLink.FamilyId);
                }
            }
        }

        if (individual.SpouseToFamilyLinks != null)
        {
            foreach (var familyLink in individual.SpouseToFamilyLinks)
            {
                if (!string.IsNullOrEmpty(familyLink.FamilyId))
                {
                    spouseOfFamilyIdsBuilder.Add(familyLink.FamilyId);
                }
            }
        }

        // Extract photo URLs from multimedia records
        var photoUrlsBuilder = ImmutableList.CreateBuilder<string>();
        if (individual.MultimediaLinks != null)
        {
            foreach (var multimediaLink in individual.MultimediaLinks)
            {
                var mediaId = GetMultimediaId(multimediaLink);
                if (!string.IsNullOrEmpty(mediaId) && multimedia.TryGetValue(mediaId, out var mediaRecord))
                {
                    var photoUrl = ExtractPhotoUrl(mediaRecord);
                    if (!string.IsNullOrEmpty(photoUrl))
                    {
                        photoUrlsBuilder.Add(photoUrl);
                    }
                }
            }
        }

        return new PersonRecord
        {
            Id = individual.IndividualId,
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

    private string? ExtractPhotoUrl(MultimediaRecord multimedia)
    {
        if (multimedia == null)
            return null;

        // Try to get file reference from multimedia record
        var files = GetMultimediaFiles(multimedia);
        if (files != null)
        {
            var file = files.FirstOrDefault();
            if (file != null)
            {
                var filename = GetFileReference(file);
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    filename = filename.Trim();

                    // Check if it's a URL (http/https)
                    if (filename.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        filename.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return filename;
                    }

                    // Check if it's a local file path that we should keep
                    return filename;
                }
            }
        }

        return null;
    }

    private void ProcessFamily(FamilyRecord family, Dictionary<string, PersonRecord> persons)
    {
        var husbandId = family.HusbandId;
        var wifeId = family.WifeId;
        var childIds = GetFamilyChildren(family)
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

    private static DateInfo? ConvertDate(DateBase? gedcomDate)
    {
        if (gedcomDate == null)
            return null;

        // Try to extract date components based on the date type
        int? year = null;
        int? month = null;
        int? day = null;
        string? originalText = null;

        if (gedcomDate is DateExact exactDate)
        {
            year = exactDate.Year;
            month = ParseMonth(exactDate.Month);
            day = exactDate.Day;
            originalText = FormatDateString(day, month, year);
        }
        else if (gedcomDate is Date simpleDate)
        {
            year = simpleDate.Year;
            month = ParseMonth(simpleDate.Month);
            day = simpleDate.Day;
            originalText = FormatDateString(day, month, year);
        }
        else if (gedcomDate is DateBetween betweenDate)
        {
            // Use first date for the main value
            if (betweenDate.Date != null)
            {
                year = betweenDate.Date.Year;
                month = ParseMonth(betweenDate.Date.Month);
                day = betweenDate.Date.Day;
            }
            originalText = $"BET {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DateAbout aboutDate)
        {
            year = aboutDate.Year;
            month = ParseMonth(aboutDate.Month);
            day = aboutDate.Day;
            originalText = $"ABT {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DateBefore beforeDate)
        {
            year = beforeDate.Year;
            month = ParseMonth(beforeDate.Month);
            day = beforeDate.Day;
            originalText = $"BEF {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DateAfter afterDate)
        {
            year = afterDate.Year;
            month = ParseMonth(afterDate.Month);
            day = afterDate.Day;
            originalText = $"AFT {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DateEstimated estimatedDate)
        {
            year = estimatedDate.Year;
            month = ParseMonth(estimatedDate.Month);
            day = estimatedDate.Day;
            originalText = $"EST {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DateCalculate calcDate)
        {
            year = calcDate.Year;
            month = ParseMonth(calcDate.Month);
            day = calcDate.Day;
            originalText = $"CAL {FormatDateString(day, month, year)}";
        }
        else if (gedcomDate is DatePhrase phraseDate)
        {
            originalText = phraseDate.Text;
            if (phraseDate.InterpretedDate != null)
            {
                year = phraseDate.InterpretedDate.Year;
                month = ParseMonth(phraseDate.InterpretedDate.Month);
                day = phraseDate.InterpretedDate.Day;
            }
        }
        else if (gedcomDate is DateFromTo fromToDate)
        {
            // Use From date as the main value
            if (fromToDate.From != null)
            {
                year = fromToDate.From.Year;
                month = ParseMonth(fromToDate.From.Month);
                day = fromToDate.From.Day;
            }
            originalText = $"FROM {FormatDateString(day, month, year)}";
        }

        if (!year.HasValue)
            return null;

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
            Original = originalText ?? FormatDateString(day, month, year),
            Date = date,
            Precision = precision
        };
    }

    private static int? ParseMonth(string? monthStr)
    {
        if (string.IsNullOrEmpty(monthStr))
            return null;

        return monthStr.ToUpperInvariant() switch
        {
            "JAN" => 1,
            "FEB" => 2,
            "MAR" => 3,
            "APR" => 4,
            "MAY" => 5,
            "JUN" => 6,
            "JUL" => 7,
            "AUG" => 8,
            "SEP" => 9,
            "OCT" => 10,
            "NOV" => 11,
            "DEC" => 12,
            _ => null
        };
    }

    private static string FormatDateString(int? day, int? month, int? year)
    {
        var parts = new List<string>();
        if (day.HasValue) parts.Add(day.Value.ToString());
        if (month.HasValue)
        {
            var monthNames = new[] { "", "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            if (month.Value >= 1 && month.Value <= 12)
                parts.Add(monthNames[month.Value]);
        }
        if (year.HasValue) parts.Add(year.Value.ToString());
        return string.Join(" ", parts);
    }

    private static string? GetPlace(PlaceStructure? place)
    {
        if (place == null)
            return null;

        // PlaceStructure.Name typically contains the full place hierarchy
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
    /// Parse a full name string in GEDCOM format "Given /Surname/"
    /// </summary>
    private static void ParseFullName(string? fullName, out string? firstName, out string? lastName)
    {
        firstName = null;
        lastName = null;

        if (string.IsNullOrWhiteSpace(fullName))
            return;

        // GEDCOM name format: "Given Names /Surname/"
        var match = Regex.Match(fullName, @"^([^/]*?)/?([^/]*)?/?$");
        if (match.Success)
        {
            firstName = CleanName(match.Groups[1].Value);
            lastName = CleanName(match.Groups[2].Value);
        }
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

    #region Helper methods for accessing version-specific properties

    private static PersonalNamePiecesStructure? GetPersonalNamePieces(PersonalNameStructure name)
    {
        // Try to get PersonalNamePieces from version-specific classes
        return name switch
        {
            Patagames.GedcomNetSdk.Structures.Ver70.PersonalName n70 => n70.PersonalNamePieces,
            Patagames.GedcomNetSdk.Structures.Ver555.PersonalName n555 => n555.PersonalNamePieces,
            Patagames.GedcomNetSdk.Structures.Ver551.PersonalName n551 => n551.PersonalNamePieces,
            Patagames.GedcomNetSdk.Structures.Ver55.PersonalName n55 => n55.PersonalNamePieces,
            _ => null
        };
    }

    private static string? GetNameType(PersonalNameStructure name)
    {
        // Try to get Type from version-specific classes
        return name switch
        {
            Patagames.GedcomNetSdk.Structures.Ver70.PersonalName n70 => n70.Type,
            Patagames.GedcomNetSdk.Structures.Ver555.PersonalName n555 => n555.Type,
            Patagames.GedcomNetSdk.Structures.Ver551.PersonalName n551 => n551.Type,
            _ => null
        };
    }

    private static string? GetAutomatedRecordId(IndividualRecord individual)
    {
        return individual switch
        {
            Patagames.GedcomNetSdk.Records.Ver70.Individual i70 => i70.AutomatedRecordId,
            Patagames.GedcomNetSdk.Records.Ver555.Individual i555 => i555.AutomatedRecordId,
            Patagames.GedcomNetSdk.Records.Ver551.Individual i551 => i551.AutomatedRecordId,
            Patagames.GedcomNetSdk.Records.Ver55.Individual i55 => i55.AutomatedRecordId,
            _ => null
        };
    }

    private static IEnumerable<object>? GetIndividualEvents(IndividualRecord individual)
    {
        return individual switch
        {
            Patagames.GedcomNetSdk.Records.Ver70.Individual i70 => i70.Events?.Cast<object>(),
            Patagames.GedcomNetSdk.Records.Ver555.Individual i555 => i555.Events?.Cast<object>(),
            Patagames.GedcomNetSdk.Records.Ver551.Individual i551 => i551.Events?.Cast<object>(),
            Patagames.GedcomNetSdk.Records.Ver55.Individual i55 => i55.Events?.Cast<object>(),
            _ => null
        };
    }

    private static string? GetEventType(object evt)
    {
        // Get the event type tag name
        var type = evt.GetType();
        var typeName = type.Name;

        // Map class names to GEDCOM tags
        return typeName switch
        {
            "EvtBirth" => "BIRT",
            "EvtDeath" => "DEAT",
            "EvtBurial" => "BURI",
            "EvtChristening" => "CHR",
            "EvtBaptism" => "BAPM",
            "EvtAdoption" => "ADOP",
            "EvtCremation" => "CREM",
            "EvtEmigration" => "EMIG",
            "EvtImmigration" => "IMMI",
            "EvtNaturalization" => "NATU",
            "EvtCensusIndividual" => "CENS",
            "EvtGraduation" => "GRAD",
            "EvtRetirement" => "RETI",
            "EvtProbate" => "PROB",
            "EvtWill" => "WILL",
            _ => null
        };
    }

    private static EventDetailStructure? GetEventDetail(object evt)
    {
        // Try to get the base event detail structure from any event
        // In Gedcom.Net.SDK, events inherit from EventDetailStructure or derived types
        if (evt is EventDetailStructure eventDetail)
        {
            return eventDetail;
        }

        // Fallback: try to find Date and Place properties via reflection
        // This handles cases where event classes don't directly inherit from EventDetailStructure
        var type = evt.GetType();

        // Check if the type has Date and Place properties
        var dateProp = type.GetProperty("Date");
        var placeProp = type.GetProperty("Place");

        if (dateProp != null || placeProp != null)
        {
            // Create a wrapper to access the properties
            return new ReflectionEventDetail(evt);
        }

        return null;
    }

    /// <summary>
    /// Wrapper class to access event properties via reflection when direct inheritance is not available
    /// </summary>
    private class ReflectionEventDetail : EventDetailStructure
    {
        private readonly object _source;
        private readonly System.Reflection.PropertyInfo? _dateProp;
        private readonly System.Reflection.PropertyInfo? _placeProp;

        public ReflectionEventDetail(object source)
        {
            _source = source;
            var type = source.GetType();
            _dateProp = type.GetProperty("Date");
            _placeProp = type.GetProperty("Place");
        }

        public new DateBase? Date => _dateProp?.GetValue(_source) as DateBase;
        public new PlaceStructure? Place => _placeProp?.GetValue(_source) as PlaceStructure;
    }

    private static IEnumerable<string> GetFamilyChildren(FamilyRecord family)
    {
        return family switch
        {
            Patagames.GedcomNetSdk.Records.Ver70.Family f70 => f70.Children?.Select(c => c.Child) ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver555.Family f555 => f555.Children ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver551.Family f551 => f551.Children ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver55.Family f55 => f55.Children ?? Enumerable.Empty<string>(),
            _ => Enumerable.Empty<string>()
        };
    }

    private static string? GetMultimediaId(MultimediaLinkStructure link)
    {
        return link.MultimediaId;
    }

    private static IEnumerable<object>? GetMultimediaFiles(MultimediaRecord media)
    {
        return media switch
        {
            Patagames.GedcomNetSdk.Records.Ver70.Multimedia m70 => m70.Files?.Cast<object>(),
            Patagames.GedcomNetSdk.Records.Ver555.Multimedia m555 => m555.Files?.Cast<object>(),
            Patagames.GedcomNetSdk.Records.Ver551.Multimedia m551 => m551.Files?.Cast<object>(),
            _ => null
        };
    }

    private static string? GetFileReference(object file)
    {
        // Try to get the file reference/path from the file object
        var type = file.GetType();
        var fileRefProp = type.GetProperty("FileReference") ?? type.GetProperty("File");
        return fileRefProp?.GetValue(file) as string;
    }

    #endregion
}
