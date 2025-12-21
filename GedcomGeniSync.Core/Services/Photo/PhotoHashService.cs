using System.Numerics;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GedcomGeniSync.Services.Photo;

public class PhotoHashService : IPhotoHashService
{
    public string ComputeContentHash(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var hash = SHA256.HashData(data);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public ulong ComputePerceptualHash(byte[] imageData)
    {
        if (imageData == null)
            throw new ArgumentNullException(nameof(imageData));

        if (imageData.Length == 0)
            throw new ArgumentException("Image data is empty.", nameof(imageData));

        using var image = Image.Load<Rgba32>(imageData);
        image.Mutate(ctx => ctx.Resize(8, 8).Grayscale());

        long total = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < 8; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                {
                    total += row[x].R;
                }
            }
        });

        var average = total / 64.0;
        ulong hash = 0;
        var bitIndex = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < 8; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                {
                    if (row[x].R > average)
                    {
                        var shift = 63 - bitIndex;
                        hash |= 1UL << shift;
                    }

                    bitIndex++;
                }
            }
        });

        return hash;
    }

    public double CompareHashes(ulong hash1, ulong hash2)
    {
        var diff = hash1 ^ hash2;
        var distance = BitOperations.PopCount(diff);
        return 1.0 - (distance / 64.0);
    }
}
