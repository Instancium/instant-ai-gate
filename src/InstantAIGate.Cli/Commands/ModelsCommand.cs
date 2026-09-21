namespace InstantAIGate.Cli.Commands;

using InstantAIGate.SSR.Contracts;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

public class ModelsCommand : IConsoleCommand
{
    private readonly IModelCatalogService _catalogService;

    public ModelsCommand(IModelCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public string Name => "/models";
    public string Description => "Lists all available models from the local catalog.";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var models = await _catalogService.GetSupportedModelsAsync(cancellationToken);

        if (models.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Catalog is empty or model_catalog.json is missing.[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Architecture");
        table.AddColumn("Quantization");
        table.AddColumn(new TableColumn("Size (GB)").RightAligned());

        foreach (var model in models)
        {
            double sizeGb = model.TotalFileSizeBytes / 1024.0 / 1024.0 / 1024.0;
            table.AddRow(
                $"[cyan]{model.Id}[/]",
                model.Name,
                model.Architecture,
                model.Quantization,
                $"[yellow]{sizeGb:F2}[/]"
            );
        }

        AnsiConsole.Write(table);
    }
}