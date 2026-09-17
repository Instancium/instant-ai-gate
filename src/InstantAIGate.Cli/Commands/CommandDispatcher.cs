using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli.Commands;

public class CommandDispatcher
{
    private readonly IEnumerable<IConsoleCommand> _commands;

    public CommandDispatcher(IEnumerable<IConsoleCommand> commands)
    {
        _commands = commands;
    }

    public async Task<bool> TryExecuteAsync(string input, CancellationToken cancellationToken)
    {
        if (!input.StartsWith('/'))
        {
            return false;
        }

        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var commandName = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : string.Empty;

        var command = _commands.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (command != null)
        {
            await command.ExecuteAsync(argument, cancellationToken);
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Unknown command. Type /help to see available commands.[/]");
        }

        return true;
    }

    public IEnumerable<IConsoleCommand> GetAllCommands() => _commands;
}