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

    // Semantic tracking methods
    void RecordMessageSpan(string sessionId, string role, int startPos, int endPos);
    bool TryCalculateSemanticEviction(string sessionId, int requiredSpace, out int evictionStart, out int evictionEnd);
    int GetAdaptiveTokenReserve(string sessionId, int staticMaxTokens);

    // Token topology tracking for Radix Tree lookup
    void UpdatePastTokensCount(string sessionId, int count);
    void AppendSessionTokens(string sessionId, int[] tokens);
    int[] GetSessionPrefixTokens(string sessionId);
    int GetPastTokensCount(string sessionId);
}