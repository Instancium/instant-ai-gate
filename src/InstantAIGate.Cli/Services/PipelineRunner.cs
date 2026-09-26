namespace InstantAIGate.Cli.Services;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class PipelineRunner
{
    public async Task RunAsync(string pipelineName, IEnumerable<IPipelineStep> steps, PipelineContext context, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"\n[bold cyan]Starting: {pipelineName}[/]");

        foreach (var step in steps)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Executing: {step.Name}...", async ctx =>
                {
                    try
                    {
                        await step.ExecuteAsync(context, ct);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error during '{step.Name}':[/] {ex.Message}");
                        throw; // Halt pipeline execution
                    }
                });

            AnsiConsole.MarkupLine($"[green]v[/] {step.Name}");
        }
    }
}