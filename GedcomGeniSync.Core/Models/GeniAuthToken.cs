using System.Diagnostics.CodeAnalysis;

namespace GedcomGeniSync.Services;

[ExcludeFromCodeCoverage]
public class GeniAuthToken
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}
