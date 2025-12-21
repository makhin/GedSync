using GedcomGeniSync.Models;

namespace GedcomGeniSync.Services.Photo;

public interface IPhotoCompareService
{
    /// <summary>
    /// Compare photos belonging to source and destination persons.
    /// </summary>
    Task<PhotoCompareReport> ComparePersonPhotosAsync(
        string sourcePersonId,
        IReadOnlyList<string> sourcePhotoUrls,
        string destinationPersonId,
        IReadOnlyList<string> destinationPhotoUrls);
}
