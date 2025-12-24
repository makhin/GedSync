using GedcomGeniSync.Models;
using GedcomGeniSync.Services.Photo;
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
    private readonly HashSet<string> _fieldsToIgnore;
    private readonly IPhotoCompareService? _photoCompareService;

    // Fields to compare according to COMPARE_COMMAND.md specification
    private static readonly string[] FieldsToCompare =
    [
        "FirstName", "LastName", "MaidenName", "MiddleName", "Nickname", "Suffix",
        "BirthDate", "DeathDate", "BurialDate",
        "BirthPlace", "DeathPlace", "BurialPlace",
        "Gender", "PhotoUrl"
    ];

    public PersonFieldComparer(
        ILogger<PersonFieldComparer> logger,
        HashSet<string>? fieldsToIgnore = null,
        IPhotoCompareService? photoCompareService = null)
    {
        _logger = logger;
        _fieldsToIgnore = fieldsToIgnore ?? new HashSet<string>();
        _photoCompareService = photoCompareService;
    }

    public ImmutableList<FieldDiff> CompareFields(PersonRecord source, PersonRecord destination)
    {
        var differences = ImmutableList.CreateBuilder<FieldDiff>();

        // Compare name fields
        CompareStringField(differences, "FirstName", source.FirstName, destination.FirstName);
        CompareStringField(differences, "LastName", source.LastName, destination.LastName);
        CompareMaidenName(differences, source, destination);
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

        // Compare photos (unless ignored)
        if (!_fieldsToIgnore.Contains("PhotoUrl"))
        {
            ComparePhotoUrls(differences, source, destination);
        }

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

    private void CompareMaidenName(
        ImmutableList<FieldDiff>.Builder differences,
        PersonRecord source,
        PersonRecord destination)
    {
        // Only add difference if source has maiden name and destination doesn't
        if (string.IsNullOrWhiteSpace(source.MaidenName))
        {
            return; // Source has no maiden name, nothing to add
        }

        if (!string.IsNullOrWhiteSpace(destination.MaidenName))
        {
            return; // Destination already has maiden name, nothing to add
        }

        // Source has maiden name, destination doesn't
        // Check if destination's LastName matches source's MaidenName
        // This happens when destination has maiden name in SURN field but no _MARNM
        if (!string.IsNullOrWhiteSpace(destination.LastName) &&
            source.MaidenName.Trim().Equals(destination.LastName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Maiden name '{MaidenName}' found in destination's LastName, not adding as difference",
                source.MaidenName);
            return; // Maiden name already exists in destination.LastName
        }

        // Source has maiden name, destination doesn't have it anywhere
        differences.Add(new FieldDiff
        {
            FieldName = "MaidenName",
            SourceValue = source.MaidenName.Trim(),
            DestinationValue = null,
            Action = FieldAction.Add
        });

        _logger.LogDebug("Field difference found: MaidenName - source has value, destination is empty");
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
        PersonRecord source,
        PersonRecord destination)
    {
        if (_photoCompareService == null)
        {
            ComparePhotoUrlsByUrl(differences, source.PhotoUrls, destination.PhotoUrls);
            return;
        }

        try
        {
            var report = _photoCompareService.ComparePersonPhotosAsync(
                    source.Id,
                    source.PhotoUrls,
                    destination.Id,
                    destination.PhotoUrls)
                .GetAwaiter()
                .GetResult();

            foreach (var newPhoto in report.NewPhotos)
            {
                differences.Add(new FieldDiff
                {
                    FieldName = "PhotoUrl",
                    SourceValue = newPhoto.Url,
                    DestinationValue = destination.PhotoUrls.FirstOrDefault(),
                    Action = FieldAction.AddPhoto,
                    LocalPhotoPath = newPhoto.LocalPath
                });
            }

            foreach (var similar in report.SimilarPhotos)
            {
                // Skip photos that are visually identical (>= 98% similarity)
                // Different content hash likely due to compression/metadata differences
                if (similar.Similarity >= 0.98)
                {
                    _logger.LogDebug(
                        "Skipping photo update for visually identical images (similarity: {Similarity:P1}): {SourceUrl} vs {DestUrl}",
                        similar.Similarity, similar.SourceUrl, similar.DestinationUrl);
                    continue;
                }

                differences.Add(new FieldDiff
                {
                    FieldName = "PhotoUrl",
                    SourceValue = similar.SourceUrl,
                    DestinationValue = similar.DestinationUrl,
                    Action = FieldAction.UpdatePhoto,
                    PhotoSimilarity = similar.Similarity,
                    LocalPhotoPath = similar.SourceLocalPath
                });
            }

            if (report.NewPhotos.Count > 0 || report.SimilarPhotos.Count > 0 || report.MatchedPhotos.Count > 0)
            {
                _logger.LogInformation(
                    "Photo comparison results: {NewCount} new, {MatchedCount} matched, {SimilarCount} similar (updates)",
                    report.NewPhotos.Count,
                    report.MatchedPhotos.Count,
                    report.SimilarPhotos.Count(s => s.Similarity < 0.98));
            }

            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Photo comparison failed; falling back to URL comparison.");
        }

        ComparePhotoUrlsByUrl(differences, source.PhotoUrls, destination.PhotoUrls);
    }

    private void ComparePhotoUrlsByUrl(
        ImmutableList<FieldDiff>.Builder differences,
        ImmutableList<string> sourcePhotos,
        ImmutableList<string> destPhotos)
    {
        _logger.LogDebug("Comparing photos: Source has {SourceCount} photo(s), Dest has {DestCount} photo(s)",
            sourcePhotos.Count, destPhotos.Count);

        // Find photos in source that are not in destination
        var newPhoto = sourcePhotos.Except(destPhotos).FirstOrDefault();

        if (newPhoto != null)
        {
            // For now, we'll report the first new photo URL
            // In the future, this could be enhanced to handle multiple photos
            differences.Add(new FieldDiff
            {
                FieldName = "PhotoUrl",
                SourceValue = newPhoto,
                DestinationValue = destPhotos.FirstOrDefault(),
                Action = FieldAction.AddPhoto
            });

            _logger.LogInformation("Photo difference found: new photo in source. URL: {Url}",
                newPhoto);
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
