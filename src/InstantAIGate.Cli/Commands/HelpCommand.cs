namespace InstantAIGate.Cli.Commands;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class HelpCommand : IConsoleCommand
{
    private readonly IServiceProvider _serviceProvider;

    public HelpCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Name => "/help";
    public string Description => "Displays this help message with a list of all available commands.";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var allCommands = _serviceProvider.GetRequiredService<IEnumerable<IConsoleCommand>>();

        // Add Expand() to force internal cell wrapping and use a cleaner border
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn("Command");
        table.AddColumn("Description");

        foreach (var command in allCommands)
        {
            table.AddRow($"[yellow]{command.Name}[/]", Markup.Escape(command.Description));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        return Task.CompletedTask;
    }
}