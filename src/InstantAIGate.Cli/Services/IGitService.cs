namespace InstantAIGate.Cli.Services;

using System.Threading;
using System.Threading.Tasks;

public interface IGitService
{
    Task AddAllAsync(CancellationToken ct);
    Task<string> GetCachedDiffAsync(CancellationToken ct);
    Task CommitAsync(string message, CancellationToken ct);
    Task PushCurrentBranchAsync(CancellationToken ct);
}