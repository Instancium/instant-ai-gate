namespace InstantAIGate.Cli.Commands;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Displays a list of all available CLI commands and their descriptions.
/// </summary>
public class HelpCommand : IConsoleCommand
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the help command.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve commands at runtime.</param>
    public HelpCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Name => "/help";

    public string Description => "Displays this help message with a list of all available commands.";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        // Resolve the commands at execution time to break the circular dependency
        var allCommands = _serviceProvider.GetRequiredService<IEnumerable<IConsoleCommand>>();

        var table = new Table();
        table.AddColumn("Command");
        table.AddColumn("Description");

        foreach (var command in allCommands)
        {
            table.AddRow($"[yellow]{command.Name}[/]", command.Description);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        return Task.CompletedTask;
    }
}