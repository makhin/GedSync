using System.Security.Cryptography;
using FluentAssertions;
using GedcomGeniSync.Services.Photo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GedcomGeniSync.Tests;

public class PhotoHashServiceTests
{
    [Fact]
    public void ComputeContentHash_ShouldMatchSha256()
    {
        var service = new PhotoHashService();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var expected = "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        var result = service.ComputeContentHash(data);

        result.Should().Be(expected);
    }

    [Fact]
    public void ComputePerceptualHash_ShouldBeStableForIdenticalImages()
    {
        var service = new PhotoHashService();
        var bytes = CreateGradientImageBytes();

        var hash1 = service.ComputePerceptualHash(bytes);
        var hash2 = service.ComputePerceptualHash(bytes);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputePerceptualHash_ShouldDifferentiateDistinctImages()
    {
        var service = new PhotoHashService();
        var gradient = CreateGradientImageBytes();
        var black = CreateSolidImageBytes(0);

        var hashGradient = service.ComputePerceptualHash(gradient);
        var hashBlack = service.ComputePerceptualHash(black);

        hashGradient.Should().NotBe(hashBlack);
        service.CompareHashes(hashGradient, hashBlack).Should().BeLessThan(1.0);
    }

    [Fact]
    public void CompareHashes_ShouldReturnExpectedSimilarity()
    {
        var service = new PhotoHashService();
        var similarity = service.CompareHashes(0b0UL, 0b1UL);

        similarity.Should().BeApproximately(0.984375, 0.0001);
    }

    private static byte[] CreateGradientImageBytes()
    {
        using var image = new Image<Rgba32>(8, 8);

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var value = (byte)((x + y) * 16);
                image[x, y] = new Rgba32(value, value, value);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSolidImageBytes(byte value)
    {
        using var image = new Image<Rgba32>(8, 8);

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image[x, y] = new Rgba32(value, value, value);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
