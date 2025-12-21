namespace GedcomGeniSync.Services.Photo;

public interface IPhotoHashService
{
    /// <summary>
    /// Compute SHA256 hash for exact content comparison.
    /// </summary>
    string ComputeContentHash(byte[] data);

    /// <summary>
    /// Compute perceptual hash for visual comparison.
    /// </summary>
    ulong ComputePerceptualHash(byte[] imageData);

    /// <summary>
    /// Compare two perceptual hashes and return similarity (0.0 - 1.0).
    /// </summary>
    double CompareHashes(ulong hash1, ulong hash2);
}
