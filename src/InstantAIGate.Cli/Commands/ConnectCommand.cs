namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public class ConnectCommand : IConsoleCommand
{
    private readonly GatewayClientProxy _proxy;

    public ConnectCommand(GatewayClientProxy proxy)
    {
        _proxy = proxy;
    }

    public string Name => "/connect";
    public string Description => "Switches between Local and Remote modes (e.g., /connect local OR /connect http://localhost:5000 [key]).";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Usage: /connect local OR /connect <url> [key][/]");
            return Task.CompletedTask;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var target = parts[0].ToLowerInvariant();

        if (target == "local")
        {
            try
            {
                _proxy.SwitchToLocal();
                AnsiConsole.MarkupLine("[green]Successfully switched to Local Inference Engine (Vulkan).[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to switch to local mode:[/] {ex.Message}");
            }
        }
        else
        {
            var url = parts[0];
            var key = parts.Length > 1 ? parts[1] : "test-admin-secret";
            try
            {
                _proxy.SwitchToRemote(url, key);
                AnsiConsole.MarkupLine($"[green]Successfully connected to Remote Gateway at {url}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to connect to remote gateway:[/] {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }
}