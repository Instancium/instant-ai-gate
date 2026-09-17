namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.State;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class LoadCommand : IConsoleCommand
{
    private readonly CliSession _session;
    private readonly IConfiguration _configuration;
    private readonly IModelManager _modelManager;

    public LoadCommand(CliSession session, IConfiguration configuration, IModelManager modelManager)
    {
        _session = session;
        _configuration = configuration;
        _modelManager = modelManager;
    }

    public string Name => "/load";
    public string Description => "Loads a specific model into VRAM (e.g., /load qwen-vl).";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Error: You must specify a model ID.[/]");
            return;
        }

        var models = _configuration.GetSection("InstantAIGate:Models").Get<List<ModelConfigurationWrapper>>() ?? new List<ModelConfigurationWrapper>();
        var targetModel = models.Find(m => m.Id.Equals(argument, StringComparison.OrdinalIgnoreCase));

        if (targetModel == null)
        {
            AnsiConsole.MarkupLine($"[red]Model '{argument}' not found in configuration.[/]");
            return;
        }

        try
        {
            // Removed AnsiConsole.Status().StartAsync to prevent GC context destruction
            // Executing linearly on the current context, exactly like the original Program.cs
            AnsiConsole.MarkupLine($"[yellow]Loading {targetModel.Name} into VRAM via Vulkan backend... Please wait.[/]");

            await _modelManager.LoadModelAsync(targetModel.Config, cancellationToken);

            _session.ActiveModelId = targetModel.Id;
            _session.ActiveModelConfig = targetModel.Config;
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