namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using System;
using System.Threading;
using System.Threading.Tasks;



public interface ISessionInferenceManager : IDisposable
{
    Task CreateSessionAsync(SessionStartRequest request, CancellationToken ct = default);

    Task<InferenceContext> GetOrCreateContextAsync(string sessionId, CancellationToken ct = default);

    Task ReleaseSessionAsync(string sessionId, CancellationToken ct = default);

    bool TryGetSessionRepoId(string sessionId, out string? repoId);

    Task<IDisposable> AcquireSessionExecutionGateAsync(string sessionId, CancellationToken ct = default);
    int GetPastTokensCount(string sessionId);

    void UpdatePastTokensCount(string sessionId, int count);

    void EnqueueTurnTokens(string sessionId, int tokenCount);
    bool TryDequeueOldestTurnTokens(string sessionId, out int tokenCount);
}
