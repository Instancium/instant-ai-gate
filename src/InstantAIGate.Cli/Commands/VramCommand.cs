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
    public string Description => "Displays runtime backend, GPU layer offloading, and execution parameters.";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        try
        {
            var details = await _gatewayClient.GetActiveModelDetailsAsync(cancellationToken);

            if (details == null || string.IsNullOrWhiteSpace(details.RepoId))
            {
                AnsiConsole.MarkupLine("[yellow]No active model is loaded in memory.[/]");
                return;
            }

            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("[cyan]Hardware Parameter[/]");
            table.AddColumn("[cyan]Active Configuration[/]");

            table.AddRow("Active Model", $"[bold]{Markup.Escape(details.RepoId)}[/]");
            table.AddRow("Inference Backend", $"[green]{Markup.Escape(details.Backend)}[/]");
            table.AddRow("Context Window (n_ctx)", $"{details.ContextSize:N0} tokens");
            table.AddRow("Offloaded GPU Layers", details.GpuLayers > 0 ? $"[green]{details.GpuLayers}[/]" : "[yellow]CPU Only (0)[/]");
            table.AddRow("Thread Concurrency", details.Threads.ToString());
            table.AddRow("Flash Attention", details.FlashAttention ? "[green]Enabled[/]" : "[yellow]Disabled[/]");
            table.AddRow("Idle Pool Contexts", details.IdleContextsCount.ToString());

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to query hardware details:[/] {Markup.Escape(ex.Message)}");
        }
    }
}