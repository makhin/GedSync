using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using PersonRecord = GedcomGeniSync.Models.PersonRecord;
using GedcomFamilyRecord = GeneGenie.Gedcom.GedcomFamilyRecord;
using Gender = GedcomGeniSync.Models.Gender;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Services;

/// <summary>
/// Result of loading a GEDCOM file.
/// </summary>
[ExcludeFromCodeCoverage]
public class GedcomLoadResult
{
    /// <summary>
    /// All persons keyed by GEDCOM ID (e.g., "@I123@").
    /// </summary>
    public Dictionary<string, PersonRecord> Persons { get; } = new();

    /// <summary>
    /// Original family records for reference.
    /// </summary>
    public Dictionary<string, GedcomFamilyRecord> Families { get; } = new();

    /// <summary>
    /// Statistics.
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
/// Extension methods for easier traversal.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GedcomLoadResultExtensions
{
    /// <summary>
    /// Get father of a person.
    /// </summary>
    public static PersonRecord? GetFather(this GedcomLoadResult result, PersonRecord person)
    {
        if (string.IsNullOrEmpty(person.FatherId))
            return null;

        result.Persons.TryGetValue(person.FatherId, out var father);
        return father;
    }

    /// <summary>
    /// Get mother of a person.
    /// </summary>
    public static PersonRecord? GetMother(this GedcomLoadResult result, PersonRecord person)
    {
        if (string.IsNullOrEmpty(person.MotherId))
            return null;

        result.Persons.TryGetValue(person.MotherId, out var mother);
        return mother;
    }

    /// <summary>
    /// Get all spouses of a person.
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
    /// Get all children of a person.
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
    /// Get all siblings of a person.
    /// </summary>
    public static IEnumerable<PersonRecord> GetSiblings(this GedcomLoadResult result, PersonRecord person)
    {
        var father = result.GetFather(person);
        var mother = result.GetMother(person);

        if (father != null)
        {
            foreach (var child in result.GetChildren(father))
            {
                if (child.Id != person.Id)
                    yield return child;
            }
        }

        if (mother != null)
        {
            foreach (var child in result.GetChildren(mother))
            {
                if (child.Id != person.Id)
                    yield return child;
            }
        }
    }
}
