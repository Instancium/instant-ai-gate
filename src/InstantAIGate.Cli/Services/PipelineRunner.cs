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
        AnsiConsole.Write(new Rule().RuleStyle("grey"));

        foreach (var step in steps)
        {
            try
            {
                // Шаги теперь сами управляют своим UI (спиннеры, меню или прямой текст)
                await step.ExecuteAsync(context, ct);
                AnsiConsole.MarkupLine($"[green]v[/] {step.Name}\n");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[bold red]Pipeline Halted at '{step.Name}':[/] {ex.Message}");
                throw;
            }
        }

        AnsiConsole.MarkupLine($"[bold green]Pipeline '{pipelineName}' completed successfully![/]");
    }
}