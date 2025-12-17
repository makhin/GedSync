using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using GedcomGeniSync.Cli;
using GedcomGeniSync.Cli.Commands;
using System.Linq;

namespace GedcomGeniSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        args = PreprocessArgs(args);

        var startup = new Startup();
        var commands = new IHostedCommand[]
        {
            new SyncCommandHandler(startup),
            new AnalyzeCommandHandler(startup),
            new CompareCommandHandler(startup),
            new WaveCompareCommandHandler(startup),
            new AuthCommandHandler(),
            new ProfileCommandHandler(startup)
        };

        var rootCommand = new RootCommand("GEDCOM to Geni synchronization tool");

        foreach (var command in commands)
        {
            rootCommand.AddCommand(command.BuildCommand());
        }

        var commandLineBuilder = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseParseErrorReporting();

        return await commandLineBuilder.Build().InvokeAsync(args);
    }

    /// <summary>
    /// Preprocesses command-line arguments to prevent System.CommandLine from treating @ symbols as response files.
    /// GEDCOM IDs like @I123@ would otherwise trigger file-not-found errors.
    /// We prepend \@ to disable response file processing for those arguments.
    /// </summary>
    private static string[] PreprocessArgs(string[] args)
    {
        return args.Select(arg =>
        {
            if (arg.StartsWith("@") && arg.EndsWith("@") && arg.Length > 2)
            {
                return "\\" + arg;
            }
            return arg;
        }).ToArray();
    }
}
