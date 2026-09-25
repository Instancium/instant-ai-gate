// File: src/InstantAIGate.Cli/Commands/DownloadCommand.cs
namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Core.Dtos.Config;
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

public class DownloadCommand : IConsoleCommand
{
    private readonly IModelCatalogService _catalogService;
    private readonly IModelDownloader _downloader;
    private readonly StorageSettings _storageSettings;

    public DownloadCommand(
        IModelCatalogService catalogService,
        IModelDownloader downloader,
        IOptions<StorageSettings> storageOptions)
    {
        _catalogService = catalogService;
        _downloader = downloader;
        _storageSettings = storageOptions.Value;
    }

    public string Name => "/download";

    public string Description => "Downloads a model by ID from the catalog (e.g., /download qwen3-vl-2b-instruct).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Error: You must specify a model ID (e.g., /download qwen3-vl-2b-instruct).[/]");
            return;
        }

        var targetModel = await _catalogService.FindModelByIdAsync(argument, cancellationToken);
        if (targetModel == null)
        {
            AnsiConsole.MarkupLine($"[red]Model '{argument}' not found in the catalog. Type /models to see available IDs.[/]");
            return;
        }

        var urlsToDownload = new List<string>(targetModel.DownloadUrls);
        if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
        {
            urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
        }

        string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, targetModel.Id);

        AnsiConsole.MarkupLine($"[blue]Starting download for '{targetModel.Name}'...[/]");
        AnsiConsole.MarkupLine($"[dim]Destination: {destinationDir}[/]");

        try
        {
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new DownloadSpeedColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var progressTask = ctx.AddTask($"[green]{targetModel.Id}[/]", new ProgressTaskSettings { MaxValue = 100 });

                    var progressReporter = new Progress<DownloadProgress>(p =>
                    {
                        progressTask.Value = p.Percentage;
                        progressTask.Description = $"[green]{targetModel.Id}[/] ({p.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s)";
                    });

                    await _downloader.DownloadModelAsync(
                        targetModel.Id,
                        urlsToDownload,
                        destinationDir,
                        progressReporter,
                        cancellationToken);
                });

            AnsiConsole.MarkupLine($"\n[green]Successfully downloaded and validated {targetModel.Name}![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Download failed:[/] {ex.Message}");
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