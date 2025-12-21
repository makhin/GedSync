using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using PersonRecord = GedcomGeniSync.Models.PersonRecord;
using Family = Patagames.GedcomNetSdk.Records.Ver551.Family;
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
    public Dictionary<string, Family> Families { get; } = new();

    /// <summary>
    /// Statistics.
    /// </summary>
    public int TotalPersons => Persons.Count;
    public int TotalFamilies => Families.Count;
    public int PersonsWithBirthDate => Persons.Values.Count(p => p.BirthDate != null);
    public int PersonsWithBirthPlace => Persons.Values.Count(p => !string.IsNullOrEmpty(p.BirthPlace));

    /// <summary>
    /// Photo download statistics (if photo download was enabled).
    /// </summary>
    public PhotoDownloadStats? PhotoStats { get; set; }

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

        if (PhotoStats != null)
        {
            logger.LogInformation("Photos: {Downloaded} downloaded, {FromCache} from cache, {Failed} failed ({Total} total) in {Duration}",
                PhotoStats.Downloaded,
                PhotoStats.FromCache,
                PhotoStats.Failed,
                PhotoStats.TotalUrls,
                PhotoStats.Duration);
        }
    }
}

public record PhotoDownloadStats
{
    public int TotalUrls { get; init; }
    public int Downloaded { get; init; }
    public int FromCache { get; init; }
    public int Failed { get; init; }
    public TimeSpan Duration { get; init; }
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
    /// Get all parents of a person.
    /// </summary>
    public static IEnumerable<PersonRecord> GetParents(this GedcomLoadResult result, PersonRecord person)
    {
        var father = result.GetFather(person);
        var mother = result.GetMother(person);

        if (father != null)
            yield return father;

        if (mother != null)
            yield return mother;
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

    /// <summary>
    /// Traverse the family graph using breadth-first search starting from the given anchor.
    /// </summary>
    /// <param name="result">The loaded GEDCOM data.</param>
    /// <param name="anchorId">Starting person ID.</param>
    /// <param name="maxDepth">Maximum depth to explore (0 = just anchor).</param>
    /// <returns>Sequence of persons visited in BFS order.</returns>
    public static IEnumerable<PersonRecord> TraverseBfs(this GedcomLoadResult result, string anchorId, int maxDepth = 3)
    {
        if (!result.Persons.TryGetValue(anchorId, out var anchor))
            yield break;

        var visited = new HashSet<string>();
        var queue = new Queue<(PersonRecord person, int depth)>();

        queue.Enqueue((anchor, 0));
        visited.Add(anchor.Id);

        while (queue.Count > 0)
        {
            var (person, depth) = queue.Dequeue();
            yield return person;

            if (depth >= maxDepth)
                continue;

            IEnumerable<PersonRecord> neighbors =
                result.GetParents(person)
                    .Concat(result.GetSpouses(person))
                    .Concat(result.GetChildren(person))
                    .Concat(result.GetSiblings(person));

            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor.Id))
                {
                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }
    }
}
