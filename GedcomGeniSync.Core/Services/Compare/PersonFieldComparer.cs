using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace GedcomGeniSync.Services.Compare;

/// <summary>
/// Service for comparing individual fields between two PersonRecord instances
/// Implements field comparison logic for GEDCOM compare command
/// </summary>
public class PersonFieldComparer : IPersonFieldComparer
{
    private readonly ILogger<PersonFieldComparer> _logger;

    // Fields to compare according to COMPARE_COMMAND.md specification
    private static readonly string[] FieldsToCompare =
    [
        "FirstName", "LastName", "MaidenName", "MiddleName", "Nickname", "Suffix",
        "BirthDate", "DeathDate", "BurialDate",
        "BirthPlace", "DeathPlace", "BurialPlace",
        "Gender", "PhotoUrl"
    ];

    public PersonFieldComparer(ILogger<PersonFieldComparer> logger)
    {
        _logger = logger;
    }

    public ImmutableList<FieldDiff> CompareFields(PersonRecord source, PersonRecord destination)
    {
        var differences = ImmutableList.CreateBuilder<FieldDiff>();

        // Compare name fields
        CompareStringField(differences, "FirstName", source.FirstName, destination.FirstName);
        CompareStringField(differences, "LastName", source.LastName, destination.LastName);
        CompareStringField(differences, "MaidenName", source.MaidenName, destination.MaidenName);
        CompareStringField(differences, "MiddleName", source.MiddleName, destination.MiddleName);
        CompareStringField(differences, "Nickname", source.Nickname, destination.Nickname);
        CompareStringField(differences, "Suffix", source.Suffix, destination.Suffix);

        // Compare date fields (with precision handling)
        CompareDateField(differences, "BirthDate", source.BirthDate, destination.BirthDate);
        CompareDateField(differences, "DeathDate", source.DeathDate, destination.DeathDate);
        CompareDateField(differences, "BurialDate", source.BurialDate, destination.BurialDate);

        // Compare place fields
        CompareStringField(differences, "BirthPlace", source.BirthPlace, destination.BirthPlace);
        CompareStringField(differences, "DeathPlace", source.DeathPlace, destination.DeathPlace);
        CompareStringField(differences, "BurialPlace", source.BurialPlace, destination.BurialPlace);

        // Compare gender
        CompareGenderField(differences, source.Gender, destination.Gender);

        // Compare photos
        ComparePhotoUrls(differences, source.PhotoUrls, destination.PhotoUrls);

        return differences.ToImmutable();
    }

    public bool AreFieldsIdentical(PersonRecord source, PersonRecord destination)
    {
        var differences = CompareFields(source, destination);
        return differences.Count == 0;
    }

    private void CompareStringField(
        ImmutableList<FieldDiff>.Builder differences,
        string fieldName,
        string? sourceValue,
        string? destValue)
    {
        // Only add difference if source has value and destination doesn't
        if (!string.IsNullOrWhiteSpace(sourceValue) && string.IsNullOrWhiteSpace(destValue))
        {
            differences.Add(new FieldDiff
            {
                FieldName = fieldName,
                SourceValue = sourceValue.Trim(),
                DestinationValue = null,
                Action = FieldAction.Add
            });

            _logger.LogDebug("Field difference found: {FieldName} - source has value, destination is empty",
                fieldName);
        }
    }

    private void CompareDateField(
        ImmutableList<FieldDiff>.Builder differences,
        string fieldName,
        DateInfo? sourceDate,
        DateInfo? destDate)
    {
        // Case 1: Source has date, destination doesn't
        if (sourceDate?.HasValue == true && destDate?.HasValue != true)
        {
            differences.Add(new FieldDiff
            {
                FieldName = fieldName,
                SourceValue = sourceDate.Original ?? sourceDate.ToGeniFormat(),
                DestinationValue = null,
                Action = FieldAction.Add
            });

            _logger.LogDebug("Date field difference found: {FieldName} - source has date, destination is empty",
                fieldName);
            return;
        }

        // Case 2: Both have dates, but source has higher precision
        if (sourceDate?.HasValue == true && destDate?.HasValue == true)
        {
            // Check if source has more precise date than destination
            if (sourceDate.Precision > destDate.Precision)
            {
                differences.Add(new FieldDiff
                {
                    FieldName = fieldName,
                    SourceValue = sourceDate.Original ?? sourceDate.ToGeniFormat(),
                    DestinationValue = destDate.Original ?? destDate.ToGeniFormat(),
                    Action = FieldAction.Update
                });

                _logger.LogDebug(
                    "Date field difference found: {FieldName} - source has higher precision ({SourcePrecision}) than destination ({DestPrecision})",
                    fieldName, sourceDate.Precision, destDate.Precision);
            }
        }
    }

    private void CompareGenderField(
        ImmutableList<FieldDiff>.Builder differences,
        Gender sourceGender,
        Gender destGender)
    {
        // Only add if source has known gender and destination doesn't
        if (sourceGender != Gender.Unknown && destGender == Gender.Unknown)
        {
            differences.Add(new FieldDiff
            {
                FieldName = "Gender",
                SourceValue = sourceGender.ToString(),
                DestinationValue = null,
                Action = FieldAction.Add
            });

            _logger.LogDebug("Gender difference found: source is {SourceGender}, destination is Unknown",
                sourceGender);
        }
    }

    private void ComparePhotoUrls(
        ImmutableList<FieldDiff>.Builder differences,
        ImmutableList<string> sourcePhotos,
        ImmutableList<string> destPhotos)
    {
        _logger.LogDebug("Comparing photos: Source has {SourceCount} photo(s), Dest has {DestCount} photo(s)",
            sourcePhotos.Count, destPhotos.Count);

        // Find photos in source that are not in destination
        var newPhotos = sourcePhotos.Except(destPhotos).ToList();

        if (newPhotos.Count > 0)
        {
            // For now, we'll report the first new photo URL
            // In the future, this could be enhanced to handle multiple photos
            differences.Add(new FieldDiff
            {
                FieldName = "PhotoUrl",
                SourceValue = newPhotos[0],
                DestinationValue = destPhotos.FirstOrDefault(),
                Action = FieldAction.AddPhoto
            });

            _logger.LogInformation("Photo difference found: {Count} new photo(s) in source. First URL: {Url}",
                newPhotos.Count, newPhotos[0]);
        }
        else if (sourcePhotos.Count > 0 && destPhotos.Count > 0)
        {
            _logger.LogDebug("Photos match between source and destination");
        }
        else if (sourcePhotos.Count == 0)
        {
            _logger.LogDebug("Source has no photos to compare");
        }
    }
}
