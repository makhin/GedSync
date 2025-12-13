using GedcomGeniSync.Core.Models.Wave;

namespace GedcomGeniSync.Core.Services.Wave;

/// <summary>
/// Методы навигации по графу генеалогического дерева.
/// Предоставляет быстрый доступ к родственникам через индексы.
/// </summary>
public static class TreeNavigator
{
    /// <summary>
    /// Получить все семьи, где персона является супругом/родителем.
    /// </summary>
    public static IEnumerable<FamilyRecord> GetFamiliesAsSpouse(TreeGraph tree, string personId)
    {
        if (tree.PersonToFamiliesAsSpouse.TryGetValue(personId, out var famIds))
            return famIds.Select(id => tree.FamiliesById[id]);
        return Enumerable.Empty<FamilyRecord>();
    }

    /// <summary>
    /// Получить все семьи, где персона является ребёнком.
    /// </summary>
    public static IEnumerable<FamilyRecord> GetFamiliesAsChild(TreeGraph tree, string personId)
    {
        if (tree.PersonToFamiliesAsChild.TryGetValue(personId, out var famIds))
            return famIds.Select(id => tree.FamiliesById[id]);
        return Enumerable.Empty<FamilyRecord>();
    }

    /// <summary>
    /// Получить всех ближайших родственников (родители, супруги, дети, сиблинги).
    /// </summary>
    public static IEnumerable<(string personId, RelationType relation)> GetImmediateRelatives(
        TreeGraph tree,
        string personId)
    {
        var relatives = new List<(string, RelationType)>();

        // Из семей как супруг: другой супруг + дети
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
        {
            // Супруг
            if (family.HusbandId != null && family.HusbandId != personId)
                relatives.Add((family.HusbandId, RelationType.Spouse));
            if (family.WifeId != null && family.WifeId != personId)
                relatives.Add((family.WifeId, RelationType.Spouse));

            // Дети
            foreach (var childId in family.ChildIds)
                relatives.Add((childId, RelationType.Child));
        }

        // Из семей как ребёнок: родители + сиблинги
        foreach (var family in GetFamiliesAsChild(tree, personId))
        {
            // Родители
            if (family.HusbandId != null)
                relatives.Add((family.HusbandId, RelationType.Parent));
            if (family.WifeId != null)
                relatives.Add((family.WifeId, RelationType.Parent));

            // Сиблинги
            foreach (var siblingId in family.ChildIds)
            {
                if (siblingId != personId)
                    relatives.Add((siblingId, RelationType.Sibling));
            }
        }

        return relatives.Distinct();
    }

    /// <summary>
    /// Получить все семьи персоны (как супруга и как ребёнка).
    /// </summary>
    public static IEnumerable<(FamilyRecord family, FamilyRole role)> GetAllFamilies(
        TreeGraph tree,
        string personId)
    {
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
            yield return (family, FamilyRole.Spouse);

        foreach (var family in GetFamiliesAsChild(tree, personId))
            yield return (family, FamilyRole.Child);
    }

    /// <summary>
    /// Получить родителей персоны.
    /// </summary>
    public static IEnumerable<string> GetParents(TreeGraph tree, string personId)
    {
        foreach (var family in GetFamiliesAsChild(tree, personId))
        {
            if (family.HusbandId != null)
                yield return family.HusbandId;
            if (family.WifeId != null)
                yield return family.WifeId;
        }
    }

    /// <summary>
    /// Получить супругов персоны.
    /// </summary>
    public static IEnumerable<string> GetSpouses(TreeGraph tree, string personId)
    {
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
        {
            if (family.HusbandId != null && family.HusbandId != personId)
                yield return family.HusbandId;
            if (family.WifeId != null && family.WifeId != personId)
                yield return family.WifeId;
        }
    }

    /// <summary>
    /// Получить детей персоны.
    /// </summary>
    public static IEnumerable<string> GetChildren(TreeGraph tree, string personId)
    {
        foreach (var family in GetFamiliesAsSpouse(tree, personId))
        {
            foreach (var childId in family.ChildIds)
                yield return childId;
        }
    }

    /// <summary>
    /// Получить сиблингов (братьев/сестёр) персоны.
    /// </summary>
    public static IEnumerable<string> GetSiblings(TreeGraph tree, string personId)
    {
        var siblings = new HashSet<string>();

        foreach (var family in GetFamiliesAsChild(tree, personId))
        {
            foreach (var childId in family.ChildIds)
            {
                if (childId != personId)
                    siblings.Add(childId);
            }
        }

        return siblings;
    }
}
