namespace InstantAIGate.Cli.Services;

using System.Threading;
using System.Threading.Tasks;

public interface IGitService
{
    Task AddAllAsync(CancellationToken ct);
    Task<string> GetCachedDiffAsync(CancellationToken ct);
    Task CommitAsync(string message, CancellationToken ct);
    Task PushCurrentBranchAsync(CancellationToken ct);

    Task<string> GetCurrentBranchAsync(CancellationToken ct);
    Task CheckoutAsync(string branchName, CancellationToken ct);
    Task PullAsync(CancellationToken ct);
    Task MergeNoFastForwardAsync(string sourceBranch, string message, CancellationToken ct);
    Task CreateAndPushTagAsync(string tagName, CancellationToken ct);
    Task PushBranchAsync(string branchName, CancellationToken ct);
    Task DeleteLocalBranchAsync(string branchName, CancellationToken ct);
    Task<string> GetLatestTagAsync(CancellationToken ct);
    Task<string> GetGitLogAsync(string fromTag, CancellationToken ct);
    Task MergeAsync(string sourceBranch, string message, CancellationToken ct);
}