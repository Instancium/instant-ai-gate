namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Pipeline.Steps;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class VersionControlCommand : IConsoleCommand
{
    private readonly IGatewayClient _gatewayClient;
    private readonly PipelineRunner _runner;
    private readonly ReleasePipelineSettings _settings;
    private readonly CliSession _session;

    // Steps for Pipeline 1 (Commit)
    private readonly GitAddAllStep _addStep;
    private readonly GenerateCommitMessageStep _generateCommitStep;
    private readonly GitCommitAndPushStep _pushStep;

    private readonly BumpVersionStep _bumpStep;
    private readonly RunUnitTestsStep _testStep;
    private readonly MergeAndPublishStep _publishStep;

    public string Name => "/vc";
    public string Description => "Opens the interactive Version Control menu (Auto-Commit & Release).";

    public VersionControlCommand(
        IGatewayClient gatewayClient,
        PipelineRunner runner,
        IOptions<ReleasePipelineSettings> settings,
        CliSession session,
        GitAddAllStep addStep,
        GenerateCommitMessageStep generateCommitStep,
        GitCommitAndPushStep pushStep, 
        BumpVersionStep bumpStep, 
        RunUnitTestsStep testStep, 
        MergeAndPublishStep publishStep)
    {
        _gatewayClient = gatewayClient;
        _runner = runner;
        _settings = settings.Value;
        _session = session;
        _addStep = addStep;
        _generateCommitStep = generateCommitStep;
        _pushStep = pushStep;
        _bumpStep = bumpStep;
        _testStep = testStep;
        _publishStep = publishStep;
    }

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]Version Control Center[/]").LeftJustified());

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Loading AI Model '{_settings.AiModelId}' into VRAM...", async ctx =>
            {
                await _gatewayClient.LoadModelAsync(_settings.AiModelId, cancellationToken);
                _session.ActiveModelId = _settings.AiModelId;
            });

        AnsiConsole.MarkupLine($"[green]Model {_settings.AiModelId} is active and ready.[/]\n");

        bool exitRequested = false;

        while (!exitRequested && !cancellationToken.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a workflow:")
                    .PageSize(5)
                    .AddChoices(new[] {
                        "1. Auto-Commit (Current Branch)",
                        "2. Publish Release (Merge to Main)",
                        "3. Exit to Main CLI"
                    }));

            switch (choice)
            {
                case "1. Auto-Commit (Current Branch)":
                    await RunCommitPipelineAsync(cancellationToken);
                    break;
                case "2. Publish Release (Merge to Main)":
                    await RunReleasePipelineAsync(cancellationToken);
                    AnsiConsole.MarkupLine("[yellow]Release pipeline not fully mapped yet.[/]");
                    break;
                case "3. Exit to Main CLI":
                    exitRequested = true;
                    break;
            }

            if (!exitRequested)
            {
                AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to the VC menu...[/]");
                System.Console.ReadKey(intercept: true);
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule("[yellow]Version Control Center[/]").LeftJustified());
            }
        }

        AnsiConsole.MarkupLine("[dim]Exiting Version Control Center...[/]");
        // The model remains in VRAM here because _session.ActiveModelId is set.
        // It can be used for standard chat in the main CLI loop.
    }

    private async Task RunCommitPipelineAsync(CancellationToken ct)
    {
        var steps = new IPipelineStep[] { _addStep, _generateCommitStep, _pushStep };
        var context = new PipelineContext();

        await _runner.RunAsync("Auto-Commit Pipeline", steps, context, ct);
    }


    private async Task RunReleasePipelineAsync(CancellationToken ct)
    {
        var steps = new IPipelineStep[] { _bumpStep, _testStep, _publishStep };
        var context = new PipelineContext();

        await _runner.RunAsync("Release Publication Pipeline", steps, context, ct);
    }
}