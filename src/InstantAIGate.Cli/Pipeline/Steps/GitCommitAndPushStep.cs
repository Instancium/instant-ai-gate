namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using System.Threading;
using System.Threading.Tasks;

public class GitCommitAndPushStep : IPipelineStep
{
    private readonly IGitService _gitService;

    public string Name => "Commit and push to remote";

    public GitCommitAndPushStep(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.CommitMessage))
        {
            throw new System.InvalidOperationException("Commit message is empty. Nothing to commit.");
        }

        await _gitService.CommitAsync(context.CommitMessage, cancellationToken);
        await _gitService.PushCurrentBranchAsync(cancellationToken);
    }
}