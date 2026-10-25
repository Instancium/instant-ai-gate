namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.SSR.Contracts;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class LoadCommand : IConsoleCommand
{
    private readonly CliSession _session;
    private readonly IGatewayClient _gatewayClient;
    private readonly IModelCatalogService _catalogService;

    public LoadCommand(CliSession session, IGatewayClient gatewayClient, IModelCatalogService catalogService)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
    }

    public string Name => "/load";
    public string Description => "Loads a model into VRAM via Gateway Client (e.g., /load qwen3-vl-8b-instruct).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage: /load <modelId> [profileName][/], e.g. /load qwen3-embedding-0.6b Embedding");
            return;
        }

        string repoId = parts[0];
        string? profile = parts.Length > 1 ? parts[1] : null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync($"Loading model '{Markup.Escape(repoId)}' with profile '{Markup.Escape(profile ?? "Default")}'...",
                    async ctx =>
                    {
                        await _gatewayClient.LoadModelAsync(repoId, profile, cancellationToken);
                        ctx.Status("[green]Model loaded successfully![/]");
                    });

            _session.ActiveModelId = repoId;
            AnsiConsole.MarkupLine($"[green]Model '{Markup.Escape(repoId)}' is ready for inference.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {Markup.Escape(ex.Message)}");
        }
    }
}