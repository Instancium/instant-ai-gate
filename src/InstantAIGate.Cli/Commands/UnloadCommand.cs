namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class UnloadCommand : IConsoleCommand
{
    private readonly CliSession _session;
    private readonly IGatewayClient _gatewayClient;

    public UnloadCommand(CliSession session, IGatewayClient gatewayClient)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    public string Name => "/unload";
    public string Description => "Unloads a specific model from VRAM (e.g., /unload qwen3-embedding-0.6b).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        string targetId = argument?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            AnsiConsole.MarkupLine("[red]Usage: /unload <modelId>[/]");
            return;
        }

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Unloading model '{Markup.Escape(targetId)}'...", async _ =>
                {
                    await _gatewayClient.UnloadModelAsync(targetId, cancellationToken);
                });

            _session.LoadedModelIds.Remove(targetId);

            if (string.Equals(_session.ActiveEmbeddingModelId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                _session.ActiveEmbeddingModelId = null;
            }

            if (string.Equals(_session.ActiveModelId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = await _gatewayClient.GetLoadedModelsDetailsAsync(cancellationToken);
                _session.ActiveModelId = remaining.FirstOrDefault()?.RepoId;
                _session.ClearHistory();
            }

            AnsiConsole.MarkupLine($"[green]Model '{Markup.Escape(targetId)}' unloaded from VRAM.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to unload model:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
