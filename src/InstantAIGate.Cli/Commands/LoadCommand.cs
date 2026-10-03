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
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Error: You must specify a model ID.[/]");
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var modelId = parts[0];

        var targetModel = await _catalogService.FindModelByIdAsync(modelId, cancellationToken);
        string displayName = targetModel != null ? targetModel.Name : modelId;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync($"Loading {displayName} into memory...", async ctx =>
                {
                    await _gatewayClient.LoadModelAsync(modelId, cancellationToken);
                });

            _session.ActiveModelId = modelId;
            _session.ClearHistory();
            AnsiConsole.MarkupLine($"[green]Model {displayName} successfully loaded and ready for inference![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {Markup.Escape(ex.Message)}");
            _session.ActiveModelId = null;
        }
    }
}