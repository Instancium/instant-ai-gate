namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class VramCommand : IConsoleCommand
{
    private readonly IGatewayClient _gatewayClient;

    public VramCommand(IGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    public string Name => "/vram";
    public string Description => "Displays backend config or purges idle VRAM pool (e.g., /vram or /vram purge).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        string subCommand = argument?.Trim().ToLowerInvariant() ?? string.Empty;

        try
        {
            if (subCommand == "purge")
            {
                await HandlePurgeAsync(cancellationToken);
            }
            else
            {
                await HandleStatusAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]VRAM operation failed:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task HandlePurgeAsync(CancellationToken ct)
    {
        var details = await _gatewayClient.GetActiveModelDetailsAsync(ct);
        if (details == null || string.IsNullOrWhiteSpace(details.RepoId))
        {
            AnsiConsole.MarkupLine("[yellow]No active model is loaded in memory to purge.[/]");
            return;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Purging idle context pool from VRAM...", async ctx =>
            {
                await _gatewayClient.PurgeIdleContextsAsync(details.RepoId, ct);
            });

        AnsiConsole.MarkupLine($"[green]Successfully purged idle context pool for model '{Markup.Escape(details.RepoId)}'. VRAM slots freed.[/]");
    }

    private async Task HandleStatusAsync(CancellationToken ct)
    {
        var details = await _gatewayClient.GetActiveModelDetailsAsync(ct);
        if (details == null || string.IsNullOrWhiteSpace(details.RepoId))
        {
            AnsiConsole.MarkupLine("[yellow]No active model is loaded in memory.[/]");
            return;
        }

        string layerText = details.TotalLayers > 0
            ? $"{Math.Min(details.GpuLayers, details.TotalLayers)} / {details.TotalLayers} layers"
            : $"{details.GpuLayers} layers";

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("[cyan]Hardware Parameter[/]");
        table.AddColumn("[cyan]Active Configuration[/]");

        table.AddRow("Active Model", $"[bold]{Markup.Escape(details.RepoId)}[/]");
        table.AddRow("Inference Backend", $"[green]{Markup.Escape(details.Backend)}[/]");
        table.AddRow("Context Window (n_ctx)", $"{details.ContextSize:N0} tokens");
        table.AddRow("Offloaded GPU Layers", $"[green]{layerText}[/]");
        table.AddRow("Thread Concurrency", details.Threads.ToString());
        table.AddRow("Flash Attention", details.FlashAttention ? "[green]Enabled[/]" : "[yellow]Disabled[/]");
        table.AddRow("Idle Pool Contexts", details.IdleContextsCount.ToString());

        AnsiConsole.Write(table);
    }
}