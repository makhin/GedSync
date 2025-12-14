using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class ProfileCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string?> _tokenOption = new("--token", description: "Geni API access token (or set GENI_ACCESS_TOKEN env var)");
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Path to saved token file from auth command");
    private readonly Option<bool?> _verboseOption = new("--verbose", description: "Enable verbose logging");

    public ProfileCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var profileCommand = new Command("profile", "Get current user's Geni profile information");

        profileCommand.AddOption(_tokenOption);
        profileCommand.AddOption(_tokenFileOption);
        profileCommand.AddOption(_verboseOption);

        profileCommand.SetHandler(HandleAsync);
        return profileCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var token = context.ParseResult.GetValueForOption(_tokenOption);
        var tokenFile = context.ParseResult.GetValueForOption(_tokenFileOption)!;
        var verbose = context.ParseResult.GetValueForOption(_verboseOption) ?? false;

        await using var scope = _startup.CreateScope(verbose, services =>
        {
            services.AddSingleton<IGeniProfileClient>(sp =>
            {
                var resolvedToken = token ?? Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");

                if (string.IsNullOrWhiteSpace(resolvedToken))
                {
                    var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile).Result;
                    if (storedToken != null && !storedToken.IsExpired)
                    {
                        resolvedToken = storedToken.AccessToken;
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedToken))
                {
                    throw new InvalidOperationException("No valid token found. Run 'auth' command first.");
                }

                return new GeniProfileClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    resolvedToken,
                    dryRun: false,
                    sp.GetRequiredService<ILogger<GeniProfileClient>>());
            });

            services.AddSingleton<IGeniPhotoClient>(sp =>
            {
                var resolvedToken = token ?? Environment.GetEnvironmentVariable("GENI_ACCESS_TOKEN");

                if (string.IsNullOrWhiteSpace(resolvedToken))
                {
                    var storedToken = GeniAuthClient.LoadTokenFromFileAsync(tokenFile).Result;
                    if (storedToken != null && !storedToken.IsExpired)
                    {
                        resolvedToken = storedToken.AccessToken;
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedToken))
                {
                    throw new InvalidOperationException("No valid token found. Run 'auth' command first.");
                }

                return new GeniPhotoClient(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    resolvedToken,
                    dryRun: false,
                    sp.GetRequiredService<ILogger<GeniPhotoClient>>());
            });

            services.AddSingleton<IGeniApiClient>(sp => new GeniApiClient(
                sp.GetRequiredService<IGeniProfileClient>(),
                sp.GetRequiredService<IGeniPhotoClient>()));
        });

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Profile");

        try
        {
            var geniClient = provider.GetRequiredService<IGeniApiClient>();
            var profile = await geniClient.GetCurrentUserProfileAsync();

            if (profile == null)
            {
                logger.LogError("Failed to get profile");
                context.ExitCode = 1;
                return;
            }

            logger.LogInformation("");
            logger.LogInformation("=== Your Geni Profile ===");
            logger.LogInformation("Name: {Name}", profile.FirstName + " " + profile.LastName);
            logger.LogInformation("Numeric ID: {Id}", profile.Id.Replace("profile-", ""));
            logger.LogInformation("GUID: {Guid}", profile.Guid);
            logger.LogInformation("");
            logger.LogInformation("For sync command use:");
            logger.LogInformation("  --anchor-geni {Id}", profile.Id.Replace("profile-", ""));

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get profile");
            context.ExitCode = 1;
        }
    }
}
