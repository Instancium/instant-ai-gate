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
    public string Description => "Displays all loaded models in VRAM or purges idle pool (/vram or /vram purge [modelId]).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var parts = (argument ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string subCommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        string? targetRepoId = parts.Length > 1 ? parts[1] : null;

        try
        {
            if (subCommand == "purge")
            {
                await HandlePurgeAsync(targetRepoId, cancellationToken);
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

    private async Task HandlePurgeAsync(string? targetRepoId, CancellationToken ct)
    {
        var models = await _gatewayClient.GetLoadedModelsDetailsAsync(ct);
        if (models.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No active models are loaded in memory to purge.[/]");
            return;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Purging idle context pool from VRAM...", async _ =>
            {
                if (!string.IsNullOrWhiteSpace(targetRepoId))
                {
                    await _gatewayClient.PurgeIdleContextsAsync(targetRepoId, ct);
                }
                else
                {
                    foreach (var m in models)
                    {
                        await _gatewayClient.PurgeIdleContextsAsync(m.RepoId, ct);
                    }
                }
            });

        AnsiConsole.MarkupLine("[green]Successfully purged idle context pool(s). VRAM slots freed.[/]");
    }

    private async Task HandleStatusAsync(CancellationToken ct)
    {
        var models = await _gatewayClient.GetLoadedModelsDetailsAsync(ct);
        if (models.Count == 0)
        {
            var single = await _gatewayClient.GetActiveModelDetailsAsync(ct);
            if (single == null || string.IsNullOrWhiteSpace(single.RepoId))
            {
                AnsiConsole.MarkupLine("[yellow]No active models are loaded in memory.[/]");
                return;
            }

            models = [single];
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("[cyan]Loaded Model[/]");
        table.AddColumn("[cyan]Backend[/]");
        table.AddColumn("[cyan]Context (n_ctx)[/]");
        table.AddColumn("[cyan]GPU Layers[/]");
        table.AddColumn("[cyan]Threads[/]");
        table.AddColumn("[cyan]Flash Attn[/]");
        table.AddColumn("[cyan]Embedding Dim[/]");
        table.AddColumn("[cyan]Idle Contexts[/]");

        foreach (var details in models.Where(d => !string.IsNullOrWhiteSpace(d.RepoId)))
        {
            string layerText = details.TotalLayers > 0
                ? $"{Math.Min(details.GpuLayers, details.TotalLayers)} / {details.TotalLayers}"
                : $"{details.GpuLayers}";

            table.AddRow(
                $"[bold]{Markup.Escape(details.RepoId)}[/]",
                $"[green]{Markup.Escape(details.Backend)}[/]",
                $"{details.ContextSize:N0}",
                $"[green]{layerText}[/]",
                details.Threads.ToString(),
                details.FlashAttention ? "[green]Enabled[/]" : "[yellow]Disabled[/]",
                details.EmbeddingDimension.ToString(),
                details.IdleContextsCount.ToString());
        }

        AnsiConsole.Write(table);
    }
}
