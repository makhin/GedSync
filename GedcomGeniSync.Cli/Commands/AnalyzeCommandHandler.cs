using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Commands;

public class AnalyzeCommandHandler : IHostedCommand
{
    private readonly Startup _startup;

    private readonly Option<string> _gedcomOption = new("--gedcom", description: "Path to GEDCOM file") { IsRequired = true };
    private readonly Option<string?> _anchorOption = new("--anchor", description: "GEDCOM ID to start BFS from (optional)");

    public AnalyzeCommandHandler(Startup startup)
    {
        _startup = startup;
    }

    public Command BuildCommand()
    {
        var analyzeCommand = new Command("analyze", "Analyze GEDCOM file without syncing");
        analyzeCommand.AddOption(_gedcomOption);
        analyzeCommand.AddOption(_anchorOption);
        analyzeCommand.SetHandler(HandleAsync);
        return analyzeCommand;
    }

    private async Task HandleAsync(InvocationContext context)
    {
        var gedcomPath = context.ParseResult.GetValueForOption(_gedcomOption)!;
        var anchor = context.ParseResult.GetValueForOption(_anchorOption);

        await using var scope = _startup.CreateScope(verbose: true);

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Analyze");

        try
        {
            logger.LogInformation("=== GEDCOM Analysis ===");

            var gedcomLoader = scope.ServiceProvider.GetRequiredService<GedcomLoader>();
            var result = gedcomLoader.Load(gedcomPath);

            result.PrintStats(logger);

            if (!string.IsNullOrEmpty(anchor))
            {
                var resolvedAnchor = GedcomIdNormalizer.Normalize(anchor);

                logger.LogInformation("\n=== BFS from {Anchor} ===", anchor);

                var count = 0;
                foreach (var person in result.TraverseBfs(resolvedAnchor, maxDepth: 3))
                {
                    var relations = new List<string>();
                    if (!string.IsNullOrEmpty(person.FatherId)) relations.Add($"father:{person.FatherId}");
                    if (!string.IsNullOrEmpty(person.MotherId)) relations.Add($"mother:{person.MotherId}");
                    if (person.SpouseIds.Any()) relations.Add($"spouses:{person.SpouseIds.Count}");
                    if (person.ChildrenIds.Any()) relations.Add($"children:{person.ChildrenIds.Count}");

                    logger.LogInformation("  {Person} [{Relations}]", person, string.Join(", ", relations));

                    count++;
                    if (count >= 50)
                    {
                        logger.LogInformation("  ... (showing first 50)");
                        break;
                    }
                }
            }

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis failed");
            context.ExitCode = 1;
        }

        await Task.CompletedTask;
    }
}
