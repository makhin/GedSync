namespace GedcomGeniSync.Services.Photo;

public static class PhotoSourceDetector
{
    private static readonly string[] MyHeritageHosts =
    {
        "myheritage.com",
        "www.myheritage.com",
        "familysearch.myheritage.com",
        "media.myheritage.com",
        "mhcache.com"
    };

    private static readonly string[] GeniHosts =
    {
        "geni.com",
        "media.geni.com"
    };

    public static string DetectSource(string url)
    {
        if (IsMyHeritageUrl(url))
            return "myheritage";

        if (IsGeniUrl(url))
            return "geni";

        return "other";
    }

    public static bool IsMyHeritageUrl(string url) => IsHostMatch(url, MyHeritageHosts);

    public static bool IsGeniUrl(string url) => IsHostMatch(url, GeniHosts);

    private static bool IsHostMatch(string url, IEnumerable<string> hosts)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return hosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }
}
