namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.State;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

public class DebugCommand : IConsoleCommand
{
    private readonly DebugState _debugState;

    public DebugCommand(DebugState debugState)
    {
        _debugState = debugState;
    }

    public string Name => "/debug";

    public string Description => "Toggles the native engine diagnostic logs (e.g., /debug on).";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        bool newState = !_debugState.IsEnabled; // Toggle if no argument is provided

        if (argument.Equals("on", System.StringComparison.OrdinalIgnoreCase)) newState = true;
        if (argument.Equals("off", System.StringComparison.OrdinalIgnoreCase)) newState = false;

        _debugState.IsEnabled = newState;

        AnsiConsole.MarkupLine(newState
            ? "[yellow]Engine debug logs are now ENABLED.[/]"
            : "[green]Engine debug logs are now DISABLED.[/]");

        return Task.CompletedTask;
    }
}