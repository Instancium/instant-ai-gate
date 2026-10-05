namespace InstantAIGate.Cli.Commands;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class HelpCommand : IConsoleCommand
{
    private readonly IServiceProvider _serviceProvider;

    private static readonly HashSet<string> ReleaseCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/vc", "/release"
    };

    public HelpCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Name => "/help";
    public string Description => "Displays structured help and available interactive CLI commands.";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var allCommands = _serviceProvider.GetRequiredService<IEnumerable<IConsoleCommand>>().ToList();

        var gatewayCommands = allCommands
            .Where(c => !ReleaseCommands.Contains(c.Name))
            .OrderBy(c => c.Name);

        var releaseCommands = allCommands
            .Where(c => ReleaseCommands.Contains(c.Name))
            .OrderBy(c => c.Name);

        AnsiConsole.WriteLine();

        var refTable = new Table()
            .Title("[bold cyan]Reference Gateway Commands[/]")
            .Border(TableBorder.Rounded)
            .Expand();

        refTable.AddColumn(new TableColumn("[yellow]Command[/]").Width(12));
        refTable.AddColumn(new TableColumn("[white]Description[/]"));

        foreach (var cmd in gatewayCommands)
        {
            refTable.AddRow($"[yellow]{cmd.Name}[/]", Markup.Escape(cmd.Description));
        }

        AnsiConsole.Write(refTable);
        AnsiConsole.WriteLine();

        var relTable = new Table()
            .Title("[bold magenta]Release & Publishing Tools[/]")
            .Border(TableBorder.Rounded)
            .Expand();

        relTable.AddColumn(new TableColumn("[yellow]Command[/]").Width(12));
        relTable.AddColumn(new TableColumn("[white]Description[/]"));

        foreach (var cmd in releaseCommands)
        {
            relTable.AddRow($"[yellow]{cmd.Name}[/]", Markup.Escape(cmd.Description));
        }

        AnsiConsole.Write(relTable);
        AnsiConsole.WriteLine();

        return Task.CompletedTask;
    }
}