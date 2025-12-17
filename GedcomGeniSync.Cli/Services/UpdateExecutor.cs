using GedcomGeniSync.ApiClient.Models;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Executes profile updates based on wave-compare results
/// </summary>
public class UpdateExecutor
{
    private readonly IGeniProfileClient _profileClient;
    private readonly IGeniPhotoClient _photoClient;
    private readonly IMyHeritagePhotoService _photoService;
    private readonly GedcomLoadResult _gedcom;
    private readonly ILogger _logger;

    public UpdateExecutor(
        IGeniProfileClient profileClient,
        IGeniPhotoClient photoClient,
        IMyHeritagePhotoService photoService,
        GedcomLoadResult gedcom,
        ILogger logger)
    {
        _profileClient = profileClient;
        _photoClient = photoClient;
        _photoService = photoService;
        _gedcom = gedcom;
        _logger = logger;
    }

    public async Task<Commands.UpdateResult> ExecuteUpdatesAsync(
        System.Collections.Immutable.ImmutableList<NodeToUpdate> nodesToUpdate,
        HashSet<string> skipFields,
        bool syncPhotos)
    {
        var result = new Commands.UpdateResult();

        foreach (var node in nodesToUpdate)
        {
            result.TotalProcessed++;

            try
            {
                _logger.LogInformation("Processing {PersonSummary} (Source: {SourceId}, Geni: {GeniId})",
                    node.PersonSummary, node.SourceId, node.GeniProfileId ?? "unknown");

                if (string.IsNullOrEmpty(node.GeniProfileId))
                {
                    _logger.LogWarning("  Skipping - no Geni Profile ID");
                    result.Failed++;
                    result.Errors.Add(new Commands.UpdateError
                    {
                        SourceId = node.SourceId,
                        GeniProfileId = "unknown",
                        FieldName = "GeniProfileId",
                        ErrorMessage = "No Geni Profile ID found"
                    });
                    continue;
                }

                // Get source person data from GEDCOM
                var sourcePerson = _gedcom.Persons.GetValueOrDefault(node.SourceId);
                if (sourcePerson == null)
                {
                    _logger.LogWarning("  Skipping - source person {SourceId} not found in GEDCOM", node.SourceId);
                    result.Failed++;
                    result.Errors.Add(new Commands.UpdateError
                    {
                        SourceId = node.SourceId,
                        GeniProfileId = node.GeniProfileId,
                        FieldName = "SourcePerson",
                        ErrorMessage = "Source person not found in GEDCOM"
                    });
                    continue;
                }

                // Process fields to update
                var updatesMade = false;
                var photoUrl = string.Empty;

                // Separate photo updates from profile updates
                var fieldsForProfile = node.FieldsToUpdate
                    .Where(f => f.Action != FieldAction.AddPhoto && !skipFields.Contains(f.FieldName))
                    .ToList();

                var photoFields = node.FieldsToUpdate
                    .Where(f => f.Action == FieldAction.AddPhoto)
                    .ToList();

                // 1. Update profile fields
                if (fieldsForProfile.Count > 0)
                {
                    var profileUpdate = MapToGeniProfileUpdate(fieldsForProfile, sourcePerson);

                    _logger.LogInformation("  Updating {Count} field(s): {Fields}",
                        fieldsForProfile.Count,
                        string.Join(", ", fieldsForProfile.Select(f => f.FieldName)));

                    var updated = await _profileClient.UpdateProfileAsync(
                        node.GeniProfileId.Replace("profile-", ""),
                        profileUpdate);

                    if (updated != null)
                    {
                        updatesMade = true;
                        _logger.LogInformation("  ✓ Profile updated successfully");
                    }
                    else
                    {
                        _logger.LogError("  ✗ Failed to update profile");
                        result.Errors.Add(new Commands.UpdateError
                        {
                            SourceId = node.SourceId,
                            GeniProfileId = node.GeniProfileId,
                            FieldName = string.Join(",", fieldsForProfile.Select(f => f.FieldName)),
                            ErrorMessage = "Profile update failed"
                        });
                    }
                }

                // 2. Upload photos
                if (syncPhotos && photoFields.Count > 0)
                {
                    foreach (var photoField in photoFields)
                    {
                        if (string.IsNullOrEmpty(photoField.SourceValue))
                            continue;

                        photoUrl = photoField.SourceValue;

                        _logger.LogInformation("  Uploading photo from {Url}", photoUrl);

                        if (!_photoService.IsMyHeritageUrl(photoUrl))
                        {
                            _logger.LogWarning("  Skipping - not a MyHeritage URL");
                            result.PhotosFailed++;
                            continue;
                        }

                        try
                        {
                            // Download photo
                            var downloadResult = await _photoService.DownloadPhotoAsync(photoUrl);
                            if (downloadResult == null || downloadResult.Data == null)
                            {
                                _logger.LogWarning("  Failed to download photo");
                                result.PhotosFailed++;
                                result.Errors.Add(new Commands.UpdateError
                                {
                                    SourceId = node.SourceId,
                                    GeniProfileId = node.GeniProfileId,
                                    FieldName = "PhotoUrl",
                                    ErrorMessage = "Failed to download photo from MyHeritage"
                                });
                                continue;
                            }

                            // Upload to Geni as mugshot
                            var uploadedPhoto = await _photoClient.SetMugshotFromBytesAsync(
                                node.GeniProfileId.Replace("profile-", ""),
                                downloadResult.Data,
                                downloadResult.FileName);

                            if (uploadedPhoto != null)
                            {
                                result.PhotosUploaded++;
                                updatesMade = true;
                                _logger.LogInformation("  ✓ Photo uploaded successfully");
                            }
                            else
                            {
                                _logger.LogWarning("  Failed to upload photo to Geni");
                                result.PhotosFailed++;
                                result.Errors.Add(new Commands.UpdateError
                                {
                                    SourceId = node.SourceId,
                                    GeniProfileId = node.GeniProfileId,
                                    FieldName = "PhotoUrl",
                                    ErrorMessage = "Failed to upload photo to Geni"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "  Error processing photo");
                            result.PhotosFailed++;
                            result.Errors.Add(new Commands.UpdateError
                            {
                                SourceId = node.SourceId,
                                GeniProfileId = node.GeniProfileId,
                                FieldName = "PhotoUrl",
                                ErrorMessage = $"Photo processing error: {ex.Message}"
                            });
                        }
                    }
                }

                if (updatesMade)
                {
                    result.Successful++;
                }
                else if (fieldsForProfile.Count == 0 && photoFields.Count == 0)
                {
                    _logger.LogInformation("  No changes needed (all fields skipped)");
                    result.Successful++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  Error processing node");
                result.Failed++;
                result.Errors.Add(new Commands.UpdateError
                {
                    SourceId = node.SourceId,
                    GeniProfileId = node.GeniProfileId ?? "unknown",
                    FieldName = "General",
                    ErrorMessage = ex.Message
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Maps FieldDiff list to GeniProfileUpdate
    /// </summary>
    private GeniProfileUpdate MapToGeniProfileUpdate(List<FieldDiff> fields, PersonRecord sourcePerson)
    {
        var update = new GeniProfileUpdate();

        foreach (var field in fields)
        {
            var value = field.SourceValue;
            if (string.IsNullOrEmpty(value))
                continue;

            switch (field.FieldName)
            {
                case "FirstName":
                    update.FirstName = value;
                    break;

                case "MiddleName":
                    update.MiddleName = value;
                    break;

                case "LastName":
                    update.LastName = value;
                    break;

                case "MaidenName":
                    update.MaidenName = value;
                    break;

                case "Suffix":
                    update.Suffix = value;
                    break;

                case "Nickname":
                    update.Nicknames = value;
                    break;

                case "Gender":
                    update.Gender = value.ToLowerInvariant() == "m" ? "male" : "female";
                    break;

                case "BirthDate":
                    update.Birth ??= new GeniEventInput();
                    update.Birth.Date = ParseDate(value);
                    break;

                case "BirthPlace":
                    update.Birth ??= new GeniEventInput();
                    update.Birth.Location = new GeniLocationInput { PlaceName = value };
                    break;

                case "DeathDate":
                    update.Death ??= new GeniEventInput();
                    update.Death.Date = ParseDate(value);
                    break;

                case "DeathPlace":
                    update.Death ??= new GeniEventInput();
                    update.Death.Location = new GeniLocationInput { PlaceName = value };
                    break;

                case "BurialDate":
                    update.Burial ??= new GeniEventInput();
                    update.Burial.Date = ParseDate(value);
                    break;

                case "BurialPlace":
                    update.Burial ??= new GeniEventInput();
                    update.Burial.Location = new GeniLocationInput { PlaceName = value };
                    break;

                case "Occupation":
                    update.Occupation = value;
                    break;

                default:
                    _logger.LogWarning("Unknown field name: {FieldName}", field.FieldName);
                    break;
            }
        }

        return update;
    }

    /// <summary>
    /// Parse GEDCOM date string to GeniDateInput
    /// Supports formats: "YYYY", "DD MMM YYYY", "YYYY-MM-DD", etc.
    /// </summary>
    private GeniDateInput? ParseDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Remove qualifiers like "ABT", "AFT", "BEF", "EST"
        dateStr = System.Text.RegularExpressions.Regex.Replace(dateStr, @"^(ABT|AFT|BEF|EST|CAL|BET)\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        var result = new GeniDateInput();

        // Try to parse year (4 digits)
        var yearMatch = System.Text.RegularExpressions.Regex.Match(dateStr, @"\b(\d{4})\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year))
        {
            result.Year = year;
        }

        // Try to parse month (as number or name)
        var monthNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "JAN", 1 }, { "FEB", 2 }, { "MAR", 3 }, { "APR", 4 },
            { "MAY", 5 }, { "JUN", 6 }, { "JUL", 7 }, { "AUG", 8 },
            { "SEP", 9 }, { "OCT", 10 }, { "NOV", 11 }, { "DEC", 12 }
        };

        foreach (var (monthName, monthNum) in monthNames)
        {
            if (dateStr.Contains(monthName, StringComparison.OrdinalIgnoreCase))
            {
                result.Month = monthNum;
                break;
            }
        }

        // Try to parse day (1-2 digits, not part of year)
        var dayMatch = System.Text.RegularExpressions.Regex.Match(dateStr, @"\b(\d{1,2})\b(?!\d)");
        if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var day) && day >= 1 && day <= 31)
        {
            result.Day = day;
        }

        // Only return if we got at least year
        return result.Year.HasValue ? result : null;
    }
}
