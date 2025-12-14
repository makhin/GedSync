using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GedcomGeniSync.ApiClient.Utils;
using GedcomGeniSync.Models;
using GedcomGeniSync.Utils;
using Microsoft.Extensions.Logging;
using Patagames.GedcomNetSdk;
using Patagames.GedcomNetSdk.Enums;
using Patagames.GedcomNetSdk.Records;
using Patagames.GedcomNetSdk.Records.Ver551;
using Patagames.GedcomNetSdk.Structures;
using Patagames.GedcomNetSdk.Structures.Ver551;
using Patagames.GedcomNetSdk.Dates;

namespace GedcomGeniSync.Services;

/// <summary>
/// Loads GEDCOM 5.5.1 files and converts to PersonRecord models
/// Uses Gedcom.Net.SDK library for parsing
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

        // Configure parser to skip unknown tags gracefully
        GedcomTransmission transmission;
        try
        {
            using var parser = new Parser(filePath);
            // Skip unknown/custom tags (ADDR in INDI, _UPD, _UID, etc.) to avoid parsing errors
            parser.Settings.SkipUnknownTag = SkipUnknownTag.All;
            transmission = new GedcomTransmission();
            transmission.Deserialize(parser);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("GEDCOM file has formatting issues. Attempting to load with pre-processing...");
            _logger.LogDebug("Error details: {Message}", ex.Message);

            // Pre-process the GEDCOM file to fix severe formatting issues
            // (malformed NOTE lines, encoding issues, etc.) that SDK can't handle
            var cleanedFilePath = PreprocessGedcomFile(filePath);
            try
            {
                using var parser = new Parser(cleanedFilePath);
                parser.Settings.SkipUnknownTag = SkipUnknownTag.All;
                transmission = new GedcomTransmission();
                transmission.Deserialize(parser);
                _logger.LogInformation("Successfully loaded GEDCOM file after pre-processing");
            }
            catch (Exception innerEx)
            {
                // Clean up temporary file
                if (File.Exists(cleanedFilePath))
                {
                    try { File.Delete(cleanedFilePath); } catch { }
                }

                throw new InvalidOperationException(
                    $"The GEDCOM file contains formatting issues that cannot be parsed. " +
                    $"Error: {ex.Message}. " +
                    $"Please check the GEDCOM file for compliance with GEDCOM 5.5.1 standard, " +
                    $"or use a GEDCOM editor to clean up the file before importing.",
                    innerEx);
            }
            finally
            {
                // Clean up temporary file
                if (File.Exists(cleanedFilePath))
                {
                    try { File.Delete(cleanedFilePath); } catch { }
                }
            }
        }

        // Extract individuals and families from records
        var individuals = new List<Individual>();
        var families = new List<Family>();
        var multimedia = new Dictionary<string, Multimedia>();

        foreach (var record in transmission.Records)
        {
            if (record is Individual individual)
            {
                individuals.Add(individual);
            }
            else if (record is Family family)
            {
                families.Add(family);
            }
            else if (record is Multimedia media)
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

    private PersonRecord ConvertIndividual(Individual individual, Dictionary<string, Multimedia> multimedia)
    {
        string? firstName = null;
        string? middleName = null;
        string? lastName = null;
        string? maidenName = null;
        string? nickname = null;
        string? suffix = null;
        var nameVariantsBuilder = ImmutableList.CreateBuilder<string>();

        // Names - use the Name collection from IndividualRecord
        var names = individual.Name;
        var primaryName = names?.FirstOrDefault();
        if (primaryName is PersonalName pn)
        {
            // Try to extract name pieces from base class NamePieces property
            if (pn.NamePieces is PersonalNamePieces pieces)
            {
                firstName = CleanName(pieces.GivenName);
                lastName = CleanName(pieces.Surname);
                nickname = CleanName(pieces.Nickname);
                suffix = CleanName(pieces.NameSuffix);

                // Extract all name variants (including transliterations) for fuzzy matching
                // E.g., "Мустафа (Mustafa)" -> both "Мустафа" and "Mustafa"
                // E.g., "Иванов\Ivanov" -> both "Иванов" and "Ivanov"
                if (!string.IsNullOrEmpty(pieces.GivenName))
                {
                    var givenVariants = ExtractNameVariants(pieces.GivenName);
                    foreach (var variant in givenVariants)
                    {
                        if (!nameVariantsBuilder.Contains(variant))
                            nameVariantsBuilder.Add(variant);
                    }
                }
                if (!string.IsNullOrEmpty(pieces.Surname))
                {
                    var surnameVariants = ExtractNameVariants(pieces.Surname);
                    foreach (var variant in surnameVariants)
                    {
                        if (!nameVariantsBuilder.Contains(variant))
                            nameVariantsBuilder.Add(variant);
                    }
                }
                if (!string.IsNullOrEmpty(pieces.Nickname))
                {
                    var nicknameVariants = ExtractNameVariants(pieces.Nickname);
                    foreach (var variant in nicknameVariants)
                    {
                        if (!nameVariantsBuilder.Contains(variant))
                            nameVariantsBuilder.Add(variant);
                    }
                }

                // GEDCOM often puts patronymic (отчество) in the GIVN field along with first name
                // e.g., "Владимир Витальевич" instead of just "Владимир"
                // Split it into FirstName and MiddleName if there are exactly 2 words
                if (!string.IsNullOrEmpty(firstName))
                {
                    var words = firstName.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 2)
                    {
                        middleName = words[1]; // Second word is patronymic (отчество)
                        firstName = words[0];  // First word is actual first name
                        _logger.LogDebug("Split GEDCOM FirstName '{Original}' into FirstName='{First}' and MiddleName='{Middle}' for {Id}",
                            string.Join(" ", words), firstName, middleName, individual.IndividualId);
                    }
                }
            }

            // If no pieces available, fall back to parsing the full name string
            if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName))
            {
                ParseFullName(pn.Name, out firstName, out lastName);
            }
        }

        // Process all names to find maiden name and store variants
        if (names != null)
        {
            foreach (var name in names)
            {
                if (name is not PersonalName personalName)
                    continue;

                var pieces = personalName.NamePieces as PersonalNamePieces;
                var nameType = personalName.Type?.ToUpperInvariant();

                // Store name variants for fuzzy matching
                // Extract all variants including transliterations (e.g., "Мустафа (Mustafa)" -> both variants)
                if (pieces != null)
                {
                    if (!string.IsNullOrEmpty(pieces.GivenName))
                    {
                        var givenVariants = ExtractNameVariants(pieces.GivenName);
                        foreach (var variant in givenVariants)
                        {
                            if (!nameVariantsBuilder.Contains(variant))
                                nameVariantsBuilder.Add(variant);
                        }
                    }
                    if (!string.IsNullOrEmpty(pieces.Surname))
                    {
                        var surnameVariants = ExtractNameVariants(pieces.Surname);
                        foreach (var variant in surnameVariants)
                        {
                            if (!nameVariantsBuilder.Contains(variant))
                                nameVariantsBuilder.Add(variant);
                        }
                    }
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

        // Fallback: if no maiden name found via TYPE, try SurnamePrefix
        if (string.IsNullOrEmpty(maidenName) && primaryName is PersonalName pnFallback)
        {
            if (pnFallback.NamePieces is PersonalNamePieces fallbackPieces)
            {
                var surnamePrefix = CleanName(fallbackPieces.SurnamePrefix);
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

        if (individual.Events != null)
        {
            foreach (var evt in individual.Events)
            {
                var eventType = evt.GetType().Name;

                switch (eventType)
                {
                    case "EvtBirth":
                        birthDate = ConvertDate(evt.Date);
                        birthPlace = evt.Place?.Name;
                        break;

                    case "EvtDeath":
                        deathDate = ConvertDate(evt.Date);
                        deathPlace = evt.Place?.Name;
                        isLiving = false;
                        break;

                    case "EvtBurial":
                        burialDate = ConvertDate(evt.Date);
                        burialPlace = evt.Place?.Name;
                        break;
                }
            }
        }

        // Check if marked as living
        if (!isLiving.HasValue)
        {
            var birthYear = birthDate?.Year;
            if (birthYear.HasValue && birthYear > System.DateTime.Now.Year - 120)
            {
                isLiving = true;
            }
        }

        // Extract attributes (OCCU, RESI, etc.)
        string? occupation = null;
        string? residenceCity = null;
        string? residenceState = null;
        string? residenceCountry = null;
        string? residenceAddress = null;

        if (individual.Attributes != null)
        {
            foreach (var attr in individual.Attributes)
            {
                var attrTypeName = attr.GetType().Name;

                // AttrOccupation contains the OCCU tag value
                if (attrTypeName == "AttrOccupation" && occupation == null)
                {
                    var valueProp = attr.GetType().GetProperty("Value");
                    if (valueProp != null)
                    {
                        occupation = valueProp.GetValue(attr)?.ToString();
                    }
                    else
                    {
                        var descProp = attr.GetType().GetProperty("Description");
                        if (descProp != null)
                        {
                            occupation = descProp.GetValue(attr)?.ToString();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(occupation))
                    {
                        _logger.LogDebug("Found Occupation '{Occupation}' for {Id}",
                            occupation, individual.IndividualId);
                    }
                }
                // AttrResidence contains the RESI tag with address info
                else if (attrTypeName == "AttrResidence" && residenceCity == null)
                {
                    // Try to get Place property which contains address details
                    var placeProp = attr.GetType().GetProperty("Place");
                    if (placeProp != null)
                    {
                        var place = placeProp.GetValue(attr);
                        if (place != null)
                        {
                            // Try to get Name property from Place
                            var nameProp = place.GetType().GetProperty("Name");
                            if (nameProp != null)
                            {
                                residenceAddress = nameProp.GetValue(place)?.ToString();
                            }
                        }
                    }

                    // Try to get Address property which may have structured address
                    var addrProp = attr.GetType().GetProperty("Address");
                    if (addrProp != null)
                    {
                        var address = addrProp.GetValue(attr);
                        if (address != null)
                        {
                            // Extract structured address components
                            var cityProp = address.GetType().GetProperty("City");
                            var stateProp = address.GetType().GetProperty("State");
                            var countryProp = address.GetType().GetProperty("Country");
                            var addrLineProp = address.GetType().GetProperty("AddressLine");

                            if (cityProp != null)
                                residenceCity = cityProp.GetValue(address)?.ToString();
                            if (stateProp != null)
                                residenceState = stateProp.GetValue(address)?.ToString();
                            if (countryProp != null)
                                residenceCountry = countryProp.GetValue(address)?.ToString();
                            if (addrLineProp != null && string.IsNullOrEmpty(residenceAddress))
                                residenceAddress = addrLineProp.GetValue(address)?.ToString();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(residenceCity) || !string.IsNullOrWhiteSpace(residenceCountry))
                    {
                        _logger.LogDebug("Found Residence '{City}, {State}, {Country}' for {Id}",
                            residenceCity, residenceState, residenceCountry, individual.IndividualId);
                    }
                }
            }
        }

        // Family links
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
                if (multimediaLink is MultimediaLink ml && !string.IsNullOrEmpty(ml.MultimediaId))
                {
                    if (multimedia.TryGetValue(ml.MultimediaId, out var mediaRecord))
                    {
                        var photoUrl = ExtractPhotoUrl(mediaRecord);
                        if (!string.IsNullOrEmpty(photoUrl))
                        {
                            photoUrlsBuilder.Add(photoUrl);
                        }
                    }
                }
            }
        }

        // Extract Geni Profile ID from RFN tag (Reference Number / PermanentRecordFileNumber)
        // RFN is used to store Geni profile identifiers like "geni:6000000012345678901"
        // SDK exposes this via PermanentRecordFileNumber property
        string? geniProfileId = null;
        if (!string.IsNullOrWhiteSpace(individual.PermanentRecordFileNumber))
        {
            geniProfileId = individual.PermanentRecordFileNumber;
            _logger.LogDebug("Found Geni Profile ID '{GeniProfileId}' from RFN tag for {Id}",
                geniProfileId, individual.IndividualId);
        }

        // Extract NOTE tags
        var notesBuilder = ImmutableList.CreateBuilder<string>();
        if (individual.Notes != null)
        {
            foreach (var noteStructure in individual.Notes)
            {
                if (noteStructure != null)
                {
                    // NoteStructure may have different properties depending on SDK version
                    // Try to get note content via ToString() or available properties
                    var noteText = noteStructure.ToString();
                    if (!string.IsNullOrWhiteSpace(noteText))
                    {
                        notesBuilder.Add(noteText.Trim());
                    }
                }
            }
        }

        // Extract custom tags (MyHeritage _UPD, _UID, RIN, etc.)
        // SDK separates CustomTags (tags starting with _) and UnknownTags (other unknown tags)
        // Both are populated when SkipUnknownTag = All
        var customTagsBuilder = ImmutableDictionary.CreateBuilder<string, string>();

        // First, extract CustomTags (tags starting with _, e.g., _UPD, _UID)
        if (individual.CustomTags != null)
        {
            foreach (var customTag in individual.CustomTags)
            {
                var tagName = customTag.Tag;
                var tagValue = customTag.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(tagName))
                    continue;

                if (!customTagsBuilder.ContainsKey(tagName))
                {
                    customTagsBuilder[tagName] = tagValue;
                }
                else
                {
                    customTagsBuilder[tagName] = customTagsBuilder[tagName] + "\n" + tagValue;
                }
            }
        }

        // Extract ADDR, EMAIL, WWW from UnknownTags
        // These appear as unknown tags when they're at the wrong level in GEDCOM
        string? email = null;
        string? website = null;

        // Then, extract UnknownTags (other non-standard tags like RIN, ADDR at wrong level, etc.)
        if (individual.UnknownTags != null)
        {
            foreach (var unknownTag in individual.UnknownTags)
            {
                // TagInfo structure: unknownTag is TagInfo type
                var tagName = unknownTag.Tag;
                var tagValue = unknownTag.Value ?? string.Empty;

                // Skip RFN since we handle it separately
                if (string.IsNullOrWhiteSpace(tagName) ||
                    tagName.Equals("RFN", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract EMAIL
                if (tagName.Equals("EMAIL", StringComparison.OrdinalIgnoreCase) && email == null)
                {
                    email = tagValue;
                    _logger.LogDebug("Found Email '{Email}' for {Id}", email, individual.IndividualId);
                }
                // Extract WWW (website)
                else if (tagName.Equals("WWW", StringComparison.OrdinalIgnoreCase) && website == null)
                {
                    website = tagValue;
                    _logger.LogDebug("Found Website '{Website}' for {Id}", website, individual.IndividualId);
                }
                // ADDR may contain email as value in some GEDCOM files
                else if (tagName.Equals("ADDR", StringComparison.OrdinalIgnoreCase) && email == null)
                {
                    // Check if it looks like an email
                    if (!string.IsNullOrEmpty(tagValue) && tagValue.Contains('@'))
                    {
                        email = tagValue;
                        _logger.LogDebug("Found Email '{Email}' from ADDR for {Id}", email, individual.IndividualId);
                    }
                }

                // Store custom tag (except EMAIL and WWW which we extract separately)
                if (!tagName.Equals("EMAIL", StringComparison.OrdinalIgnoreCase) &&
                    !tagName.Equals("WWW", StringComparison.OrdinalIgnoreCase))
                {
                    if (!customTagsBuilder.ContainsKey(tagName))
                    {
                        customTagsBuilder[tagName] = tagValue;
                    }
                    else
                    {
                        // If tag appears multiple times, concatenate with newline
                        customTagsBuilder[tagName] = customTagsBuilder[tagName] + "\n" + tagValue;
                    }
                }
            }
        }

        return new PersonRecord
        {
            Id = individual.IndividualId,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            MiddleName = middleName,
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
            Occupation = occupation,
            Email = email,
            Website = website,
            ResidenceCity = residenceCity,
            ResidenceState = residenceState,
            ResidenceCountry = residenceCountry,
            ResidenceAddress = residenceAddress,
            ChildOfFamilyIds = childOfFamilyIdsBuilder.ToImmutable(),
            SpouseOfFamilyIds = spouseOfFamilyIdsBuilder.ToImmutable(),
            PhotoUrls = photoUrlsBuilder.ToImmutable(),
            GeniProfileId = geniProfileId,
            Notes = notesBuilder.ToImmutable(),
            CustomTags = customTagsBuilder.ToImmutable()
        };
    }

    private static string? ExtractPhotoUrl(Multimedia multimedia)
    {
        if (multimedia?.Files == null)
            return null;

        var file = multimedia.Files.FirstOrDefault();
        if (file != null && !string.IsNullOrWhiteSpace(file.File))
        {
            return file.File.Trim();
        }

        return null;
    }

    private void ProcessFamily(Family family, Dictionary<string, PersonRecord> persons)
    {
        var husbandId = family.HusbandId;
        var wifeId = family.WifeId;
        var childIds = family.Children?
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList() ?? new List<string>();

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
                // Invalid day for the month, try month precision
                try
                {
                    precision = DatePrecision.Month;
                    date = new DateOnly(year.Value, month.Value, 1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Invalid month or year, fall back to year precision
                    precision = DatePrecision.Year;
                    date = new DateOnly(year.Value, 1, 1);
                }
            }
        }
        else if (month.HasValue)
        {
            precision = DatePrecision.Month;
            try
            {
                date = new DateOnly(year.Value, month.Value, 1);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid month or year, fall back to year precision
                precision = DatePrecision.Year;
                date = new DateOnly(year.Value, 1, 1);
            }
        }
        else
        {
            precision = DatePrecision.Year;
            try
            {
                date = new DateOnly(year.Value, 1, 1);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid year, can't create any valid date - return null
                return null;
            }
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

    /// <summary>
    /// Extract name variants from a name string that may contain transliterations
    /// Supports formats:
    /// - "Мустафа (Mustafa)" -> ["Мустафа", "Mustafa"]
    /// - "Иванов\Ivanov" -> ["Иванов", "Ivanov"]
    /// - "Simple Name" -> ["Simple Name"]
    /// </summary>
    private static List<string> ExtractNameVariants(string? name)
    {
        var variants = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            return variants;

        // Pattern 1: Extract name with parentheses "Мустафа (Mustafa)"
        var parenthesesMatch = Regex.Match(name, @"^(.+?)\s*\(([^)]+)\)\s*$");
        if (parenthesesMatch.Success)
        {
            var primary = CleanNameSimple(parenthesesMatch.Groups[1].Value);
            var transliteration = CleanNameSimple(parenthesesMatch.Groups[2].Value);

            if (!string.IsNullOrWhiteSpace(primary))
                variants.Add(primary);
            if (!string.IsNullOrWhiteSpace(transliteration))
                variants.Add(transliteration);

            return variants;
        }

        // Pattern 2: Extract name with backslash "Иванов\Ivanov"
        var backslashMatch = Regex.Match(name, @"^(.+?)\\(.+)$");
        if (backslashMatch.Success)
        {
            var primary = CleanNameSimple(backslashMatch.Groups[1].Value);
            var transliteration = CleanNameSimple(backslashMatch.Groups[2].Value);

            if (!string.IsNullOrWhiteSpace(primary))
                variants.Add(primary);
            if (!string.IsNullOrWhiteSpace(transliteration))
                variants.Add(transliteration);

            return variants;
        }

        // No special format - just clean and return
        var cleaned = CleanNameSimple(name);
        if (!string.IsNullOrWhiteSpace(cleaned))
            variants.Add(cleaned);

        return variants;
    }

    /// <summary>
    /// Clean a name by removing GEDCOM markers and special characters
    /// Returns the primary (first) variant if transliterations are present
    /// </summary>
    private static string? CleanName(string? name)
    {
        var variants = ExtractNameVariants(name);
        return variants.FirstOrDefault();
    }

    /// <summary>
    /// Simple cleaning without extracting variants
    /// </summary>
    private static string CleanNameSimple(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

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


    /// <summary>
    /// Pre-process GEDCOM file to fix severe formatting issues
    /// Note: SDK's SkipUnknownTag handles non-standard tags, but we still need to fix malformed NOTE lines
    /// </summary>
    private string PreprocessGedcomFile(string filePath)
    {
        _logger.LogInformation("Pre-processing GEDCOM file to fix formatting issues");

        var tempFilePath = Path.GetTempFileName();

        // Detect encoding by reading first few bytes to detect BOM
        using var encodingReader = new StreamReader(filePath, true);
        encodingReader.Peek(); // Force detection
        var encoding = encodingReader.CurrentEncoding;

        using (var reader = new StreamReader(filePath, encoding))
        using (var writer = new StreamWriter(tempFilePath, false, encoding))
        {
            string? line;
            var lineNumber = 0;
            int? currentNoteLevel = null;  // Track if we're inside a NOTE block
            int fixedLines = 0;

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                var originalLine = line;

                // Handle empty lines - keep them but don't reset NOTE context
                if (string.IsNullOrWhiteSpace(line))
                {
                    writer.WriteLine(line);
                    continue;
                }

                // Check if this is a proper GEDCOM line (level + tag)
                var gedcomLineMatch = Regex.Match(line, @"^(\d+)\s+([A-Z_@][A-Z0-9_@]*)(\s|$)");
                if (gedcomLineMatch.Success)
                {
                    var level = int.Parse(gedcomLineMatch.Groups[1].Value);
                    var tag = gedcomLineMatch.Groups[2].Value;

                    // Check if we're starting a NOTE block
                    if (tag == "NOTE" || tag == "CONC" || tag == "CONT")
                    {
                        if (tag == "NOTE")
                        {
                            currentNoteLevel = level;
                        }
                    }
                    else
                    {
                        // New tag at same or lower level ends the NOTE context
                        if (currentNoteLevel.HasValue && level <= currentNoteLevel.Value)
                        {
                            currentNoteLevel = null;
                        }
                    }

                    writer.WriteLine(line);
                    continue;
                }

                // If we're here, the line doesn't start with a proper GEDCOM level+tag
                // If we're in a NOTE context, this should be a continuation line
                if (currentNoteLevel.HasValue)
                {
                    // This line is part of a NOTE but doesn't have proper formatting
                    // Convert it to a CONT (continuation) line at the appropriate level
                    line = $"{currentNoteLevel.Value + 1} CONT {originalLine}";
                    fixedLines++;
                    if (fixedLines <= 10)  // Log first 10 fixes
                    {
                        _logger.LogDebug("Fixed untagged NOTE content at line {LineNumber}: {Preview}...",
                            lineNumber, originalLine.Substring(0, Math.Min(60, originalLine.Length)));
                    }
                }

                writer.WriteLine(line);
            }

            if (fixedLines > 0)
            {
                _logger.LogInformation("Fixed {Count} malformed NOTE continuation lines", fixedLines);
            }
        }

        _logger.LogInformation("Pre-processing complete. Temporary file: {Path}", tempFilePath);
        return tempFilePath;
    }
}
