using GedcomGeniSync.Models;
using System.Collections.Immutable;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing individual fields between two PersonRecord instances
/// </summary>
public interface IPersonFieldComparer
{
    /// <summary>
    /// Compare two PersonRecord instances and return list of field differences
    /// Only returns differences where source has data and destination is missing/less precise
    /// </summary>
    /// <param name="source">Source person record (e.g., from MyHeritage)</param>
    /// <param name="destination">Destination person record (e.g., from Geni)</param>
    /// <returns>List of field differences to apply</returns>
    ImmutableList<FieldDiff> CompareFields(PersonRecord source, PersonRecord destination);

    /// <summary>
    /// Check if two PersonRecord instances are identical in all compared fields
    /// </summary>
    /// <param name="source">Source person record</param>
    /// <param name="destination">Destination person record</param>
    /// <returns>True if all fields match, false otherwise</returns>
    bool AreFieldsIdentical(PersonRecord source, PersonRecord destination);
}
