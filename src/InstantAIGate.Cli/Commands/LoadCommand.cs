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
    public string Description => "Loads a model into VRAM pool via Gateway Client (e.g., /load qwen3-vl-8b-instruct [profile]).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage: /load <modelId> [profileName][/], e.g. /load qwen3-embedding-0.6b Embedding");
            return;
        }

        string repoId = parts[0];
        bool isEmbedding = repoId.Contains("embed", StringComparison.OrdinalIgnoreCase);
        string? profile = parts.Length > 1 ? parts[1] : (isEmbedding ? "Embedding" : "Default");

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync($"Loading model '{Markup.Escape(repoId)}' with profile '{Markup.Escape(profile)}'...",
                    async ctx =>
                    {
                        await _gatewayClient.LoadModelAsync(repoId, profile, cancellationToken);
                        ctx.Status("[green]Model loaded successfully![/]");
                    });

            _session.LoadedModelIds.Add(repoId);

            if (string.Equals(profile, "Embedding", StringComparison.OrdinalIgnoreCase) || isEmbedding)
            {
                _session.ActiveEmbeddingModelId = repoId;
                if (string.IsNullOrEmpty(_session.ActiveModelId))
                {
                    _session.ActiveModelId = repoId;
                }

                AnsiConsole.MarkupLine($"[green]Embedding model '{Markup.Escape(repoId)}' loaded into pool and ready for /embed.[/]");
            }
            else
            {
                _session.ActiveModelId = repoId;
                AnsiConsole.MarkupLine($"[green]Model '{Markup.Escape(repoId)}' is loaded and set as active chat model.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
