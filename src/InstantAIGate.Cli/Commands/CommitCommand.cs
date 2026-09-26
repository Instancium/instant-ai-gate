namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Pipeline.Steps;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using System.Threading;
using System.Threading.Tasks;

public class CommitCommand : IConsoleCommand
{
    private readonly PipelineRunner _runner;
    private readonly GitAddAllStep _addStep;
    private readonly GenerateCommitMessageStep _generateStep;
    private readonly GitCommitAndPushStep _pushStep;

    public string Name => "/commit";
    public string Description => "Auto-generates an English commit message and pushes changes to the current branch.";

    public CommitCommand(
        PipelineRunner runner,
        GitAddAllStep addStep,
        GenerateCommitMessageStep generateStep,
        GitCommitAndPushStep pushStep)
    {
        _runner = runner;
        _addStep = addStep;
        _generateStep = generateStep;
        _pushStep = pushStep;
    }

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var steps = new IPipelineStep[] { _addStep, _generateStep, _pushStep };
        var context = new PipelineContext();

        await _runner.RunAsync("Auto-Commit Pipeline", steps, context, cancellationToken);
    }
}