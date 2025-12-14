using System.CommandLine;

namespace GedcomGeniSync.Cli.Commands;

public interface IHostedCommand
{
    Command BuildCommand();
}
