using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using GedcomGeniSync.ApiClient.Services;
using GedcomGeniSync.ApiClient.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class AuthCommandHandler : IHostedCommand
{
    private readonly Option<string?> _appKeyOption = new("--app-key", description: "Geni app key (or set GENI_APP_KEY env var)");
    private readonly Option<string> _tokenFileOption = new("--token-file", () => "geni_token.json", description: "Path to save token");
    private readonly Option<bool?> _verboseOption = new("--verbose", description: "Enable verbose logging");

    public Command BuildCommand()
    {
        var authCommand = new Command("auth", "Authenticate with Geni using Desktop OAuth and save access token");

        authCommand.AddOption(_appKeyOption);
        authCommand.AddOption(_tokenFileOption);
        authCommand.AddOption(_verboseOption);

        authCommand.SetHandler(HandleAsync);
        return authCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var appKey = context.ParseResult.GetValueForOption(_appKeyOption);
        var tokenFile = context.ParseResult.GetValueForOption(_tokenFileOption)!;
        var verbose = context.ParseResult.GetValueForOption(_verboseOption) ?? false;

        context.ExitCode = await RunAuthAsync(appKey, tokenFile, verbose, context.GetCancellationToken());
    }

    private static async Task<int> RunAuthAsync(string? appKey, string tokenFile, bool verbose, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("Auth");

        appKey ??= Environment.GetEnvironmentVariable("GENI_APP_KEY");

        if (string.IsNullOrEmpty(appKey))
        {
            logger.LogError("App key required. Use --app-key or set GENI_APP_KEY environment variable");
            return 1;
        }

        logger.LogInformation("=== Geni Authentication (Desktop OAuth) ===");

        IGeniAuthClient authClient = new GeniAuthClient(appKey, logger);

        var existingToken = await authClient.LoadTokenAsync(tokenFile);
        if (existingToken != null && !existingToken.IsExpired)
        {
            logger.LogInformation("Valid token already exists at {Path}", tokenFile);
            logger.LogInformation("Access token: {Token}...", existingToken.AccessToken[..Math.Min(20, existingToken.AccessToken.Length)]);
            logger.LogInformation("Expires at: {ExpiresAt}", existingToken.ExpiresAt);
            return 0;
        }

        var token = await authClient.LoginInteractiveAsync(cancellationToken);

        if (token == null)
        {
            logger.LogError("Authentication failed");
            return 1;
        }

        await authClient.SaveTokenAsync(token, tokenFile);

        logger.LogInformation("Access token: {Token}...", token.AccessToken[..Math.Min(20, token.AccessToken.Length)]);
        logger.LogInformation("Saved token to {Path}", tokenFile);
        logger.LogInformation("Use this token with --token option or set GENI_ACCESS_TOKEN env var");

        return 0;
    }
}
