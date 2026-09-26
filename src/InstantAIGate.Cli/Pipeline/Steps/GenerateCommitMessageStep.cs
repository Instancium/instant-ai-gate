namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.Services.Analysis;
using InstantAIGate.Cli.State;
using System.Threading;
using System.Threading.Tasks;

public class GenerateCommitMessageStep : IPipelineStep
{
    private readonly IGitService _gitService;
    private readonly IDiffAnalyzer _diffAnalyzer;

    public string Name => "Analyze Diff and Generate Commit Message";

    public GenerateCommitMessageStep(IGitService gitService, IDiffAnalyzer diffAnalyzer)
    {
        _gitService = gitService;
        _diffAnalyzer = diffAnalyzer;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        string diff = await _gitService.GetCachedDiffAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(diff))
        {
            context.CommitMessage = "chore: auto-commit updates";
            return;
        }

        context.CommitMessage = await _diffAnalyzer.AnalyzeAndSummarizeAsync(diff, cancellationToken);

        Spectre.Console.AnsiConsole.MarkupLine($"[green]Generated Message:[/] {context.CommitMessage}");
    }
}