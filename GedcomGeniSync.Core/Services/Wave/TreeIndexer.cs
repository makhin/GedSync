using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using Patagames.GedcomNetSdk.Records.Ver551;
using Patagames.GedcomNetSdk.Structures;
using Patagames.GedcomNetSdk.Structures.Ver551;
using FamilyRecord = GedcomGeniSync.Core.Models.Wave.FamilyRecord;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Построение индексов для графа генеалогического дерева.
/// Создаёт обратные индексы для быстрого доступа к связям.
/// </summary>
public class TreeIndexer
{
    private readonly ILogger<TreeIndexer>? _logger;

    public TreeIndexer(ILogger<TreeIndexer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Построить граф дерева с индексами из результата загрузки GEDCOM.
    /// </summary>
    public TreeGraph BuildIndex(GedcomGeniSync.Services.GedcomLoadResult loadResult)
    {
        _logger?.LogInformation("Building tree index for {PersonCount} persons and {FamilyCount} families",
            loadResult.Persons.Count, loadResult.Families.Count);

        var personToFamiliesAsSpouse = new Dictionary<string, List<string>>();
        var personToFamiliesAsChild = new Dictionary<string, List<string>>();

        // Конвертируем Family из SDK в наш FamilyRecord
        var familyRecords = new Dictionary<string, FamilyRecord>();

        foreach (var (famId, sdkFamily) in loadResult.Families)
        {
            var familyRecord = ConvertToFamilyRecord(sdkFamily);
            familyRecords[famId] = familyRecord;

            // Строим обратные индексы по семьям

            // Индекс: супруг → семьи
            if (familyRecord.HusbandId != null)
            {
                if (!personToFamiliesAsSpouse.ContainsKey(familyRecord.HusbandId))
                    personToFamiliesAsSpouse[familyRecord.HusbandId] = new List<string>();
                personToFamiliesAsSpouse[familyRecord.HusbandId].Add(famId);
            }

            if (familyRecord.WifeId != null)
            {
                if (!personToFamiliesAsSpouse.ContainsKey(familyRecord.WifeId))
                    personToFamiliesAsSpouse[familyRecord.WifeId] = new List<string>();
                personToFamiliesAsSpouse[familyRecord.WifeId].Add(famId);
            }

            // Индекс: ребёнок → семьи
            foreach (var childId in familyRecord.ChildIds)
            {
                if (!personToFamiliesAsChild.ContainsKey(childId))
                    personToFamiliesAsChild[childId] = new List<string>();
                personToFamiliesAsChild[childId].Add(famId);
            }
        }

        // Опциональные индексы для ускорения fuzzy match
        var personsByBirthYear = BuildBirthYearIndex(loadResult.Persons);
        var personsByLastName = BuildLastNameIndex(loadResult.Persons);

        _logger?.LogInformation("Built indexes: {SpouseCount} person-to-spouse-families, {ChildCount} person-to-child-families",
            personToFamiliesAsSpouse.Count, personToFamiliesAsChild.Count);

        return new TreeGraph
        {
            PersonsById = loadResult.Persons,
            FamiliesById = familyRecords,
            PersonToFamiliesAsSpouse = personToFamiliesAsSpouse
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
            PersonToFamiliesAsChild = personToFamiliesAsChild
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
            PersonsByBirthYear = personsByBirthYear,
            PersonsByNormalizedLastName = personsByLastName
        };
    }

    /// <summary>
    /// Конвертировать Family из SDK в наш FamilyRecord.
    /// </summary>
    private FamilyRecord ConvertToFamilyRecord(Family sdkFamily)
    {
        // Extract child IDs - Family.Children is already IEnumerable<string>
        var childIds = sdkFamily.Children?
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList() ?? new List<string>();

        // For now, we'll skip event extraction as it requires more investigation of the SDK API
        // This can be enhanced later
        DateInfo? marriageDate = null;
        string? marriagePlace = null;
        DateInfo? divorceDate = null;

        return new FamilyRecord
        {
            Id = sdkFamily.FamilyId,  // Use FamilyId property
            HusbandId = sdkFamily.HusbandId,
            WifeId = sdkFamily.WifeId,
            ChildIds = childIds,
            MarriageDate = marriageDate,
            MarriagePlace = marriagePlace,
            DivorceDate = divorceDate
        };
    }

    /// <summary>
    /// Построить индекс: год рождения → список персон.
    /// </summary>
    private Dictionary<int, IReadOnlyList<string>> BuildBirthYearIndex(Dictionary<string, PersonRecord> persons)
    {
        return persons.Values
            .Where(p => p.BirthYear.HasValue)
            .GroupBy(p => p.BirthYear!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Id).ToList()
            );
    }

    /// <summary>
    /// Построить индекс: нормализованная фамилия → список персон.
    /// </summary>
    private Dictionary<string, IReadOnlyList<string>> BuildLastNameIndex(Dictionary<string, PersonRecord> persons)
    {
        return persons.Values
            .Where(p => !string.IsNullOrEmpty(p.NormalizedLastName))
            .GroupBy(p => p.NormalizedLastName!)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Id).ToList()
            );
    }
}
