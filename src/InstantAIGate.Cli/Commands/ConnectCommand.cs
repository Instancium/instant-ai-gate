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
    private readonly CliSession _session;
    private readonly RemoteGatewaySettings _settings;

    public ConnectCommand(
        GatewayClientProxy proxy,
        CliSession session,
        IOptions<RemoteGatewaySettings> settings)
    {
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "/connect";
    public string Description => "Connects to a remote gateway instance or switches to local engine. Usage: /connect [baseUrl] [apiKey] or /connect local";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        string trimmed = argument?.Trim() ?? string.Empty;

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

        string baseUrl = _settings.BaseUrl;
        string key = _settings.ApiKey;

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            baseUrl = parts[0];
            key = parts.Length > 1 ? parts[1] : _settings.ApiKey;
        }

        try
        {
            await _proxy.SwitchToRemoteAsync(baseUrl, key, cancellationToken);
            _session.IsRemoteMode = true;
            AnsiConsole.MarkupLine($"[green]Successfully connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Connection failed:[/] Service at [cyan]{Markup.Escape(baseUrl)}[/] is not reachable.");
            AnsiConsole.MarkupLine($"[dim red]Details:[/] {Markup.Escape(ex.Message)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect:[/] {Markup.Escape(ex.Message)}");
        }
    }
}