namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISessionInferenceManager
{
    Task CreateSessionAsync(SessionStartRequest request, CancellationToken ct = default);
    Task ReleaseSessionAsync(string sessionId, bool destroySlot = false, CancellationToken ct = default);
    bool TryGetSessionRepoId(string sessionId, out string? repoId);
    Task<IDisposable> AcquireSessionExecutionGateAsync(string sessionId, CancellationToken ct = default);
    Task<InferenceContext> GetOrCreateContextAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// KV-CACHE MANAGEMENT ARCHITECTURAL SUMMARY:
    /// -------------------------------------------------------------------------------------------------------------------------
    /// Mechanism        | FlashAttention / GPU-Offload | Data Integrity Guarantee | Runtime Application Strategy
    /// -------------------------------------------------------------------------------------------------------------------------
    /// Rollback         | Fully Supported Hardware     | 100% Deterministic       | Canonical primitive for KV-checkpointing,
    /// (seq_rm p0..-1)  | (Zero-Mutation / Invariant)  | (Prefix stays pinned)    | multi-turn resets, and Map-Reduce chunks.
    /// -------------------------------------------------------------------------------------------------------------------------
    /// Shift            | NOT Supported                | Conditional / Guarded    | CPU / Plain KV-cache only. Throws
    /// (seq_rm+seq_add) | (llama_memory_can_shift=0)   | (Physical data stays)    | NotSupportedException on GPU/FA configurations.
    /// -------------------------------------------------------------------------------------------------------------------------
    /// PrefixTree Pool  | Fully Compatible             | Isolated Slots           | Radix-tree caching for shared system prompts
    ///                  |                              |                          | across concurrent sessions (zero-copy clone).
    /// -------------------------------------------------------------------------------------------------------------------------
    /// Rollbacks context memory to the specified position by removing the KV-cache suffix [targetTokenPosition, PastTokens).
    /// Guarantees prefix immutability under both CPU and GPU/FlashAttention execution modes.
    /// </summary>
    /// <param name="sessionId">The unique stateful session identifier.</param>
    /// <param name="targetTokenPosition">Target past token count (must be less than or equal to current PastTokens).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if targetTokenPosition is negative or exceeds PastTokens.</exception>
    Task RollbackToPositionAsync(string sessionId, int targetTokenPosition, CancellationToken ct = default);

    /// <summary>
    /// Evicts an intermediate KV memory range and shifts the remaining suffix backward.
    /// <para>
    /// <b>PHYSICS NOTICE &amp; FAIL-SAFE GUARD:</b> When FlashAttention is enabled or GPU offloading is active,
    /// <c>llama.cpp</c> utilizes a unified flat KV buffer where physical memory cells cannot be shifted (<c>can_shift == false</c>).
    /// Invoking shift under these profiles silently corrupts the context or invalidates the cache.
    /// This method enforces a pre-flight guard check via <see cref="IBackendFacade.CanShiftContextMemory"/> and will throw
    /// <see cref="NotSupportedException"/> if hardware shift is unsupported. Callers must fall back to rollback and re-decode.
    /// </para>
    /// </summary>
    /// <param name="sessionId">The unique stateful session identifier.</param>
    /// <param name="startPos">The starting token offset of the range to remove.</param>
    /// <param name="count">The number of tokens to remove from the range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="NotSupportedException">Thrown when physical KV-cache shifting is rejected by native backend.</exception>
    Task ShiftMemoryRangeAsync(string sessionId, int startPos, int count, CancellationToken ct = default);

    void UpdatePastTokensCount(string sessionId, int count);
    void AppendSessionTokens(string sessionId, int[] tokens);
    int[] GetSessionPrefixTokens(string sessionId);
    int GetPastTokensCount(string sessionId);
}