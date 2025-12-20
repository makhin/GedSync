using System;
using GedcomGeniSync.ApiClient.Services;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

public static class AccessTokenResolver
{
    public static string ResolveFromFile(string tokenFile, ILogger? logger = null)
    {
        var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile, logger)
            .GetAwaiter()
            .GetResult();

        if (storedToken == null || storedToken.IsExpired)
        {
            throw new InvalidOperationException("No valid token found. Run 'auth' command first.");
        }

        return storedToken.AccessToken;
    }

    public static string Resolve(string? token, string tokenFile, ILogger? logger = null)
    {
        var resolvedToken = token ?? Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");

        if (string.IsNullOrWhiteSpace(resolvedToken))
        {
            var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile, logger)
                .GetAwaiter()
                .GetResult();

            if (storedToken != null && !storedToken.IsExpired)
            {
                resolvedToken = storedToken.AccessToken;
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedToken))
        {
            throw new InvalidOperationException("No valid token found. Run 'auth' command first.");
        }

        return resolvedToken;
    }
}
