namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class ConnectCommand : IConsoleCommand
{
    private readonly GatewayClientProxy _proxy;
    private readonly RemoteGatewaySettings _settings;
    private readonly CliSession _session;

    public ConnectCommand(GatewayClientProxy proxy, IOptions<RemoteGatewaySettings> settings, CliSession session)
    {
        _proxy = proxy;
        _settings = settings.Value;
        _session = session;
    }

    public string Name => "/connect";
    public string Description => "Connects to server using config defaults, custom URL, or switches to local. Usage: /connect OR /connect <url> [[key]] OR /connect local";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var trimmed = (argument ?? string.Empty).Trim();

        if (string.Equals(trimmed, "local", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _proxy.SwitchToLocal();
                _session.IsRemoteMode = false;
                AnsiConsole.MarkupLine("[green]Switched to Local Inference Engine.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to switch to local mode:[/] {Markup.Escape(ex.Message)}");
            }
            return;
        }

        string publicUrl = _settings.PublicUrl;
        string adminHubUrl = _settings.AdminHubUrl;
        string key = _settings.AdminKey;

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            publicUrl = parts[0];
            key = parts.Length > 1 ? parts[1] : _settings.AdminKey;
            adminHubUrl = $"{publicUrl.TrimEnd('/')}/hub/telemetry".Replace("5000", "5001");
        }

        try
        {
            await _proxy.SwitchToRemoteAsync(publicUrl, adminHubUrl, key, cancellationToken);
            _session.IsRemoteMode = true;
            AnsiConsole.MarkupLine($"[green]Successfully connected to Remote Gateway at[/] [cyan]{publicUrl}[/]");
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Connection failed:[/] Service at [cyan]{publicUrl}[/] is not reachable.");
            AnsiConsole.MarkupLine($"[dim red]Details:[/] {Markup.Escape(ex.Message)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect:[/] {Markup.Escape(ex.Message)}");
        }
    }
}