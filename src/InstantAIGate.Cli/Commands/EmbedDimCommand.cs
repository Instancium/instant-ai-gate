namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class EmbedDimCommand : IConsoleCommand
{
    private readonly IGatewayClient _gatewayClient;
    private readonly CliSession _session;

    public EmbedDimCommand(IGatewayClient gatewayClient, CliSession session)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string Name => "/embed-dim";
    public string Description => "Returns the embedding dimension of the active embedding model (e.g., /embed-dim [modelId]).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        string? explicitModel = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();
        string? modelId = explicitModel ?? _session.ActiveEmbeddingModelId ?? _session.ActiveModelId;

        if (!_session.IsRemoteMode && string.IsNullOrEmpty(modelId))
        {
            AnsiConsole.MarkupLine("[red]No model is currently loaded. Use /load <id> first.[/]");
            return;
        }

        string targetModel = modelId ?? string.Empty;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync($"Querying embedding dimension for '{Markup.Escape(targetModel)}'...", async ctx =>
                {
                    int dimension = await _gatewayClient.GetEmbeddingDimensionAsync(targetModel, cancellationToken);
                    ctx.Status("Embedding dimension retrieved!");

                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn("[cyan]Property[/]")
                        .AddColumn("[cyan]Value[/]");

                    table.AddRow("Model", Markup.Escape(string.IsNullOrEmpty(targetModel) ? "auto-resolved" : targetModel));
                    table.AddRow("Embedding Dimension", $"[bold green]{dimension}[/]");
                    table.AddRow("Vector Size (F32)", $"{dimension * 4} bytes");

                    AnsiConsole.Write(table);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to get embedding dimension:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
