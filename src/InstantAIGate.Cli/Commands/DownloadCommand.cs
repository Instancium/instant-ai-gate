namespace InstantAIGate.Cli.Commands;

using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class DownloadCommand : IConsoleCommand
{
    private readonly IModelCatalogService _catalogService;
    private readonly IModelDownloader _downloader;

    public DownloadCommand(IModelCatalogService catalogService, IModelDownloader downloader)
    {
        _catalogService = catalogService;
        _downloader = downloader;
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

        // Collect all URLs (Main weights + optional Vision Projector)
        var urlsToDownload = new List<string>(targetModel.DownloadUrls);
        if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
        {
            urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
        }

        // Define target directory relative to the CLI executable
        string destinationDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", targetModel.Id);

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
                    var progressTask = ctx.AddTask($"[green]{targetModel.Id}[/]", new ProgressTaskSettings
                    {
                        MaxValue = 100
                    });

                    // Adapter: IProgress<DownloadProgress> to Spectre.Console ProgressTask
                    var progressReporter = new Progress<DownloadProgress>(p =>
                    {
                        progressTask.Value = p.Percentage;
                        // Dynamically update speed description
                        progressTask.Description = $"[green]{targetModel.Id}[/] ({p.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s)";
                    });

                    // Trigger the SSR Pipeline
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

// Custom Spectre.Console column to format our SpeedBytesPerSecond nicely
internal sealed class DownloadSpeedColumn : ProgressColumn
{
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        return new Markup(task.IsFinished ? "[green]Complete[/]" : string.Empty);
    }
}