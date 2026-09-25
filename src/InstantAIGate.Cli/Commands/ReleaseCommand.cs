using System;
using System.Collections.Generic;
using System.Text;


namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Services;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

public class ReleaseCommand : IConsoleCommand
{
    private readonly ReleasePipelineService _pipelineService;

    public ReleaseCommand(ReleasePipelineService pipelineService)
    {
        _pipelineService = pipelineService;
    }

    public string Name => "/release";
    public string Description => "Starts the interactive 9-step release pipeline.";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]InstantAIGate Release Pipeline[/]").LeftJustified());
        await _pipelineService.ExecutePipelineAsync(cancellationToken);
    }
}
