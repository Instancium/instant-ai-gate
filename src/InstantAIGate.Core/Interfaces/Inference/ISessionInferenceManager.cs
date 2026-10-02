namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISessionInferenceManager
{
    Task CreateSessionAsync(SessionStartRequest request, CancellationToken ct = default);
    Task ReleaseSessionAsync(string sessionId, CancellationToken ct = default);
    bool TryGetSessionRepoId(string sessionId, out string? repoId);
    Task<IDisposable> AcquireSessionExecutionGateAsync(string sessionId, CancellationToken ct = default);
    Task<InferenceContext> GetOrCreateContextAsync(string sessionId, CancellationToken ct = default);

    Task RollbackToPositionAsync(string sessionId, int targetTokenPosition, CancellationToken ct = default);
    Task ShiftMemoryRangeAsync(string sessionId, int startPos, int count, CancellationToken ct = default);

    void UpdatePastTokensCount(string sessionId, int count);
    void AppendSessionTokens(string sessionId, int[] tokens);
    int[] GetSessionPrefixTokens(string sessionId);
    int GetPastTokensCount(string sessionId);
}