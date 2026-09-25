namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.State;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.SSR.Contracts;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public class LoadCommand : IConsoleCommand
{
    private readonly CliSession _session;
    private readonly IConfiguration _configuration;
    private readonly IModelManager _modelManager;
    private readonly IModelCatalogService _catalogService;

    // Внедряем IModelCatalogService вместо статического списка конфигураций
    public LoadCommand(CliSession session, IConfiguration configuration, IModelManager modelManager, IModelCatalogService catalogService)
    {
        _session = session;
        _configuration = configuration;
        _modelManager = modelManager;
        _catalogService = catalogService;
    }

    public string Name => "/load";

    public string Description => "Loads a specific model into VRAM (e.g., /load qwen3-vl-8b-instruct [[profile]]).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Error: You must specify a model ID.[/]");
            return;
        }

 
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var modelId = parts[0];
        var profileName = parts.Length > 1 ? parts[1] : "Default";


        var targetModel = await _catalogService.FindModelByIdAsync(modelId, cancellationToken);
        if (targetModel == null)
        {
            AnsiConsole.MarkupLine($"[red]Model '{modelId}' not found in the catalog. Use /models to see available IDs.[/]");
            return;
        }
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}").Get<HardwareProfileSettings>();
        if (hwProfile == null)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Hardware profile '{profileName}' not found. Using safe defaults.[/]");
            hwProfile = new HardwareProfileSettings(); 
        }


        var modelConfig = new ModelSettings
        {
            RepoId = targetModel.Id,
            VisionSupport = targetModel.RequiresVisionProjector,
            Type = targetModel.RequiresVisionProjector ? ModelType.Vlm : ModelType.Llm,

            GpuLayerCount = hwProfile.GpuLayerCount,
            MainGPU = hwProfile.MainGPU,
            ContextSize = hwProfile.ContextSize,
            BatchSize = hwProfile.BatchSize,
            Threads = hwProfile.Threads,
            FlashAttention = hwProfile.FlashAttention,
            Embeddings = hwProfile.Embeddings,
            KvCacheQuantization = hwProfile.KvCacheQuantization,
            UseMemoryLock = hwProfile.UseMemoryLock,
            MaxContexts = hwProfile.MaxContexts
        };

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync($"Loading {targetModel.Name} into VRAM via {profileName} profile...", async ctx =>
                {
                    await _modelManager.LoadModelAsync(modelConfig, cancellationToken);
                });

            _session.ActiveModelId = targetModel.Id;
            _session.ActiveModelConfig = modelConfig;
            _session.ClearHistory();

            AnsiConsole.MarkupLine($"[green]Model {targetModel.Name} successfully loaded and ready for inference![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {ex.Message}");
            _session.ActiveModelId = null;
            _session.ActiveModelConfig = null;
        }
    }
}