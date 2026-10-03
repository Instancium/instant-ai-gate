namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class DownloadCommand : IConsoleCommand
{
    private readonly IModelCatalogService _catalogService;
    private readonly IModelDownloader _downloader;
    private readonly IGatewayClient _gatewayClient;
    private readonly CliSession _session;
    private readonly StorageSettings _storageSettings;

    public DownloadCommand(
        IModelCatalogService catalogService,
        IModelDownloader downloader,
        IGatewayClient gatewayClient,
        CliSession session,
        IOptions<StorageSettings> storageOptions)
    {
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _storageSettings = storageOptions.Value;
    }

    public string Name => "/download";
    public string Description => "Downloads a model by ID from catalog (e.g., /download qwen3-vl-2b-instruct).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Error: You must specify a model ID (e.g., /download qwen3-vl-2b-instruct).[/]");
            return;
        }

        var targetModel = await _catalogService.FindModelByIdAsync(argument.Trim(), cancellationToken);
        if (targetModel == null)
        {
            AnsiConsole.MarkupLine($"[red]Model '{argument}' not found in catalog. Type /models to view list.[/]");
            return;
        }

        if (_session.IsRemoteMode)
        {
            await DownloadRemoteAsync(targetModel, cancellationToken);
        }
        else
        {
            await DownloadLocalAsync(targetModel, cancellationToken);
        }
    }

    private async Task DownloadRemoteAsync(CatalogModelEntry targetModel, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[blue]Triggering remote download on server for '{targetModel.Name}'...[/]");

        try
        {
            await _gatewayClient.SubscribeToModelDownloadAsync(targetModel.Id, ct);
            await _gatewayClient.DownloadModelAsync(targetModel.Id, ct);

            var tcs = new TaskCompletionSource();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new DownloadSpeedColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[cyan]{targetModel.Id}[/]", new ProgressTaskSettings { MaxValue = 100 });

                    await _gatewayClient.ConnectTelemetryAsync(
                        onMetrics: _ => { },
                        onSsrProgress: p =>
                        {
                            if (p.ModelId.Equals(targetModel.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                task.Value = p.Percentage;
                                task.Description = $"[cyan]{targetModel.Id}[/] ({p.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s)";
                                if (p.Percentage >= 100f)
                                {
                                    tcs.TrySetResult();
                                }
                            }
                        },
                        ct);

                    await tcs.Task;
                });

            await _gatewayClient.UnsubscribeFromModelDownloadAsync(targetModel.Id, CancellationToken.None);
            AnsiConsole.MarkupLine($"\n[green]Server successfully downloaded and verified {targetModel.Name}![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Remote download error:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task DownloadLocalAsync(CatalogModelEntry targetModel, CancellationToken ct)
    {
        var urlsToDownload = new List<string>(targetModel.DownloadUrls);
        if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
        {
            urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
        }

        string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, targetModel.Id);
        AnsiConsole.MarkupLine($"[blue]Starting local download for '{targetModel.Name}'...[/]");
        AnsiConsole.MarkupLine($"[dim]Destination: {destinationDir}[/]");

        try
        {
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new DownloadSpeedColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[green]{targetModel.Id}[/]", new ProgressTaskSettings { MaxValue = 100 });
                    var reporter = new Progress<DownloadProgress>(p =>
                    {
                        task.Value = p.Percentage;
                        task.Description = $"[green]{targetModel.Id}[/] ({p.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s)";
                    });

                    await _downloader.DownloadModelAsync(
                        targetModel.Id,
                        urlsToDownload,
                        destinationDir,
                        reporter,
                        ct);
                });

            AnsiConsole.MarkupLine($"\n[green]Successfully downloaded and validated {targetModel.Name}![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Download failed:[/] {Markup.Escape(ex.Message)}");
        }
    }
}

internal sealed class DownloadSpeedColumn : ProgressColumn
{
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        return new Markup(task.IsFinished ? "[green]Complete[/]" : string.Empty);
    }
}