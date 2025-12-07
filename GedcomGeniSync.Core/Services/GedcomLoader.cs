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
using Patagames.GedcomNetSdk.Primitives;

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
            ExtractNamePieces(primaryName, out firstName, out lastName, out nickname, out suffix, out _);

            // If no pieces available, fall back to parsing the full name string
            if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName))
            {
                ParseFullName(primaryName.Name, out firstName, out lastName);
            }
        }

        // Process all names to find maiden name and store variants
        if (names != null)
        {
            foreach (var name in names)
            {
                string? givenName = null, surname = null, nick = null, suff = null, surnamePrefix = null;
                ExtractNamePieces(name, out givenName, out surname, out nick, out suff, out surnamePrefix);

                var nameType = GetNameType(name);

                // Store name variants for fuzzy matching
                if (!string.IsNullOrEmpty(givenName))
                    nameVariantsBuilder.Add(givenName);
                if (!string.IsNullOrEmpty(surname))
                    nameVariantsBuilder.Add(surname);

                // Look for maiden name from TYPE=MAIDEN or TYPE=BIRTH
                if (nameType == "MAIDEN" || nameType == "BIRTH")
                {
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
                    if (!string.IsNullOrEmpty(surname))
                    {
                        lastName = surname;
                        _logger.LogDebug("Found married name '{MarriedName}' from NAME with TYPE=MARRIED for {Id}",
                            lastName, individual.IndividualId);
                    }
                }
            }
        }

        // Fallback: if no maiden name found via TYPE, try SurnamePrefix
        if (string.IsNullOrEmpty(maidenName) && primaryName != null)
        {
            ExtractNamePieces(primaryName, out _, out _, out _, out _, out var surnamePrefix);
            if (!string.IsNullOrEmpty(surnamePrefix))
            {
                maidenName = surnamePrefix;
                _logger.LogDebug("Using SurnamePrefix '{SurnamePrefix}' as fallback maiden name for {Id}",
                    maidenName, individual.IndividualId);
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
                var (date, place) = GetEventDateAndPlace(evt);

                switch (eventType)
                {
                    case "BIRT":
                        birthDate = ConvertDate(date);
                        birthPlace = place;
                        break;

                    case "DEAT":
                        deathDate = ConvertDate(date);
                        deathPlace = place;
                        isLiving = false;
                        break;

                    case "BURI":
                        burialDate = ConvertDate(date);
                        burialPlace = place;
                        break;
                }
            }
        }

        // Check if marked as living
        if (!isLiving.HasValue)
        {
            // If no death date and birth year suggests they could be alive
            var birthYear = birthDate?.Year;
            if (birthYear.HasValue && birthYear > System.DateTime.Now.Year - 120)
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
                var mediaId = GetMultimediaLinkId(multimediaLink);
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

    private static void ExtractNamePieces(PersonalNameStructure name, out string? givenName, out string? surname,
        out string? nickname, out string? suffix, out string? surnamePrefix)
    {
        givenName = null;
        surname = null;
        nickname = null;
        suffix = null;
        surnamePrefix = null;

        // Try to get NamePieces from the name structure
        var namePieces = name.NamePieces;
        if (namePieces == null)
            return;

        // Access version-specific PersonalNamePieces properties
        switch (namePieces)
        {
            case Patagames.GedcomNetSdk.Structures.Ver70.PersonalNamePieces p70:
                givenName = CleanName(p70.GivenName);
                surname = CleanName(p70.Surname);
                nickname = CleanName(p70.Nickname);
                suffix = CleanName(p70.NameSuffix);
                surnamePrefix = CleanName(p70.SurnamePrefix);
                break;
            case Patagames.GedcomNetSdk.Structures.Ver555.PersonalNamePieces p555:
                givenName = CleanName(p555.GivenName);
                surname = CleanName(p555.Surname);
                nickname = CleanName(p555.Nickname);
                suffix = CleanName(p555.NameSuffix);
                surnamePrefix = CleanName(p555.SurnamePrefix);
                break;
            case Patagames.GedcomNetSdk.Structures.Ver551.PersonalNamePieces p551:
                givenName = CleanName(p551.GivenName);
                surname = CleanName(p551.Surname);
                nickname = CleanName(p551.Nickname);
                suffix = CleanName(p551.NameSuffix);
                surnamePrefix = CleanName(p551.SurnamePrefix);
                break;
            case Patagames.GedcomNetSdk.Structures.Ver55.PersonalNamePieces p55:
                givenName = CleanName(p55.GivenName);
                surname = CleanName(p55.Surname);
                nickname = CleanName(p55.Nickname);
                suffix = CleanName(p55.NameSuffix);
                surnamePrefix = CleanName(p55.SurnamePrefix);
                break;
        }
    }

    private string? ExtractPhotoUrl(MultimediaRecord multimedia)
    {
        if (multimedia == null)
            return null;

        // Try to get file reference from version-specific multimedia records
        switch (multimedia)
        {
            case Patagames.GedcomNetSdk.Records.Ver70.Multimedia m70:
                if (m70.Files != null)
                {
                    var file = m70.Files.FirstOrDefault();
                    if (file != null && !string.IsNullOrWhiteSpace(file.File))
                        return file.File.Trim();
                }
                break;

            case Patagames.GedcomNetSdk.Records.Ver555.Multimedia m555:
                if (m555.File != null && !string.IsNullOrWhiteSpace(m555.File.File))
                    return m555.File.File.Trim();
                break;

            case Patagames.GedcomNetSdk.Records.Ver551.Multimedia m551:
                if (m551.Files != null)
                {
                    var file = m551.Files.FirstOrDefault();
                    if (file != null && !string.IsNullOrWhiteSpace(file.File))
                        return file.File.Trim();
                }
                break;

            case Patagames.GedcomNetSdk.Records.Ver55.Multimedia m55:
                // Ver55 uses inline Blob, not external files
                // Title might contain a reference
                if (!string.IsNullOrWhiteSpace(m55.Title))
                    return m55.Title.Trim();
                break;
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
            husband = updatedHusband;
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

        int? year = null;
        int? month = null;
        int? day = null;
        string? originalText = null;

        // Extract date components based on date type
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

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Remove GEDCOM-specific markers and special characters
        // Keep only letters (Latin and Cyrillic), digits, spaces, and hyphens
        name = Regex.Replace(name, @"[^A-Za-zА-Яа-яЁё0-9 -]", "");
        name = Regex.Replace(name, @"\s+", " ");

        return name.Trim();
    }

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

    private static string? GetNameType(PersonalNameStructure name)
    {
        // Get Type from version-specific PersonalName classes
        Patagames.GedcomNetSdk.Enums.NameType? nameType = name switch
        {
            Patagames.GedcomNetSdk.Structures.Ver70.PersonalName n70 => n70.Type,
            Patagames.GedcomNetSdk.Structures.Ver555.PersonalName n555 => n555.Type,
            Patagames.GedcomNetSdk.Structures.Ver551.PersonalName n551 => n551.Type,
            _ => null
        };

        // Convert enum to string for comparison
        return nameType?.ToString()?.ToUpperInvariant();
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
        var typeName = evt.GetType().Name;

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

    private static (DateBase? date, string? place) GetEventDateAndPlace(object evt)
    {
        // Events inherit from EventDetailStructure which has Date and Place
        if (evt is EventDetailStructure eventDetail)
        {
            var place = eventDetail.Place?.Name;
            return (eventDetail.Date, place);
        }

        // Fallback: use reflection
        var type = evt.GetType();
        var dateProp = type.GetProperty("Date");
        var placeProp = type.GetProperty("Place");

        var date = dateProp?.GetValue(evt) as DateBase;
        var placeObj = placeProp?.GetValue(evt);
        var place2 = placeObj is PlaceStructure ps ? ps.Name : null;

        return (date, place2);
    }

    private static IEnumerable<string> GetFamilyChildren(FamilyRecord family)
    {
        return family switch
        {
            Patagames.GedcomNetSdk.Records.Ver70.Family f70 =>
                f70.Children?.Select(c => c.Child) ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver555.Family f555 =>
                f555.Children ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver551.Family f551 =>
                f551.Children ?? Enumerable.Empty<string>(),
            Patagames.GedcomNetSdk.Records.Ver55.Family f55 =>
                f55.Children ?? Enumerable.Empty<string>(),
            _ => Enumerable.Empty<string>()
        };
    }

    private static string? GetMultimediaLinkId(MultimediaLinkStructure link)
    {
        // Get MultimediaId from version-specific MultimediaLink classes
        return link switch
        {
            Patagames.GedcomNetSdk.Structures.Ver70.MultimediaLink ml70 => ml70.MultimediaId,
            Patagames.GedcomNetSdk.Structures.Ver555.MultimediaLink ml555 => ml555.MultimediaId,
            Patagames.GedcomNetSdk.Structures.Ver551.MultimediaLink ml551 => ml551.MultimediaId,
            Patagames.GedcomNetSdk.Structures.Ver55.MultimediaLink ml55 => ml55.MultimediaId,
            _ => null
        };
    }

    #endregion
}
