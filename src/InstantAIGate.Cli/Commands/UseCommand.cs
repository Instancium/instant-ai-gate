namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class UseCommand : IConsoleCommand
{
    private readonly CliSession _session;
    private readonly IGatewayClient _gatewayClient;

    public UseCommand(CliSession session, IGatewayClient gatewayClient)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    public string Name => "/use";
    public string Description => "Switches the active CLI model among loaded models in VRAM (e.g., /use qwen3-vl-8b-instruct).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var loaded = await _gatewayClient.GetLoadedModelsDetailsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(argument))
        {
            if (loaded.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No models are currently loaded in VRAM. Use /load <id> first.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[cyan]Current Chat Model:[/] [bold]{Markup.Escape(_session.ActiveModelId ?? "none") }[/]");
            AnsiConsole.MarkupLine($"[cyan]Current Embedding Model:[/] [bold]{Markup.Escape(_session.ActiveEmbeddingModelId ?? "auto") }[/]");
            AnsiConsole.MarkupLine($"[dim]Loaded models: {string.Join(", ", loaded.Select(m => m.RepoId))}[/]");
            return;
        }

        string targetId = argument.Trim();
        var match = loaded.FirstOrDefault(m => m.RepoId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            AnsiConsole.MarkupLine($"[red]Model '{Markup.Escape(targetId)}' is not loaded in memory. Use /load {Markup.Escape(targetId)} first.[/] ");
            return;
        }

        _session.ActiveModelId = match.RepoId;
        _session.ClearHistory();
        AnsiConsole.MarkupLine($"[green]Switched active model to '[bold]{Markup.Escape(match.RepoId)}[/]'. New session started.[/]");
    }
}
