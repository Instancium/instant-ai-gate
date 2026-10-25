namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Spectre.Console;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class EmbedCommand : IConsoleCommand
{
    private readonly IGatewayClient _gatewayClient;
    private readonly CliSession _session;

    public EmbedCommand(IGatewayClient gatewayClient, CliSession session)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string Name => "/embed";

    public string Description => "Generates embeddings for the given text (e.g., /embed Hello world).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Usage: /embed <text>[/]");
            return;
        }

        if (!_session.IsRemoteMode && string.IsNullOrEmpty(_session.ActiveModelId))
        {
            AnsiConsole.MarkupLine("[red]No model is currently loaded. Use /load <id> first.[/]");
            return;
        }

        string modelId = _session.ActiveModelId ?? string.Empty;
        var request = new EmbeddingRequest(argument, Normalize: true);

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync($"Generating embeddings for '{Markup.Escape(argument)}'...", async ctx =>
                {
                    var response = await _gatewayClient.GenerateEmbeddingsAsync(modelId, request, cancellationToken);

                    ctx.Status($"Embeddings generated successfully!");

                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn("[cyan]Property[/]")
                        .AddColumn("[cyan]Value[/]");

                    table.AddRow("Model", Markup.Escape(modelId));
                    table.AddRow("Input Text", Markup.Escape(argument));
                    table.AddRow("Token Count", response.TokenCount.ToString());
                    table.AddRow("Embedding Dimension", response.EmbeddingDimension.ToString());

                    var preview = response.Embedding.Take(10).Select(v => v.ToString("F4")).ToArray();
                    var previewStr = string.Join(", ", preview);
                    if (response.Embedding.Length > 10)
                    {
                        previewStr += $", ... ({response.Embedding.Length - 10} more)";
                    }
                    table.AddRow("Preview", $"[dim]{previewStr}[/]");

                    double norm = Math.Sqrt(response.Embedding.Sum(v => v * v));
                    table.AddRow("L2 Norm", norm.ToString("F4"));

                    AnsiConsole.Write(table);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to generate embeddings:[/] {Markup.Escape(ex.Message)}");
        }
    }
}