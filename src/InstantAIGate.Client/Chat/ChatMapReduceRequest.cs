namespace InstantAIGate.Client.Chat;

using System;
using System.Collections.Generic;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Declarative description of a checkpointed Map-Reduce run. Chunking and prompt construction
/// remain the caller's domain responsibility; the SDK owns only the KV-cache lifecycle,
/// hierarchical aggregation, and failure recovery mechanics.
/// </summary>
public sealed record ChatMapReduceRequest
{
    /// <summary>
    /// Gets the shared prefix message (typically a system prompt) evaluated once and pinned
    /// into the KV-cache. Every map, reduce, and final turn reuses this warm prefix.
    /// </summary>
    public required ChatMessage WarmupMessage { get; init; }

    /// <summary>
    /// Gets the ordered per-chunk prompts. Each message is evaluated independently against the
    /// pinned prefix, and the KV-cache is rolled back after every turn to prevent cross-chunk
    /// contamination. Must contain at least one message.
    /// </summary>
    public required IReadOnlyList<ChatMessage> MapMessages { get; init; }

    /// <summary>
    /// Gets the factory that builds the final prompt from the aggregated summary text.
    /// The final turn streams its response to the caller.
    /// </summary>
    public required Func<string, ChatMessage> FinalPromptFactory { get; init; }

    /// <summary>
    /// Gets an optional factory that builds intermediate compression prompts during tree
    /// reduction. When <c>null</c>, a built-in generic compression prompt is used.
    /// </summary>
    public Func<string, ChatMessage>? ReducePromptFactory { get; init; }

    /// <summary>
    /// Gets the maximum character length of the aggregated summaries before hierarchical
    /// tree reduction is engaged. Callers should derive this from the model context window.
    /// Defaults to 6000.
    /// </summary>
    public int MaxAggregatedChars { get; init; } = 6000;

    /// <summary>
    /// Gets the number of partial summaries compressed per intermediate reduction turn.
    /// Must be at least 2 to guarantee reduction progress. Defaults to 3.
    /// </summary>
    public int ReduceGroupSize { get; init; } = 3;

    /// <summary>
    /// Gets the maximum number of attempts per turn when the gateway session is lost
    /// (reconnect mid-turn, server-side eviction, failed rollback). Recovery recreates the
    /// session and re-warms the prefix; already collected summaries are preserved.
    /// Must be at least 1. Defaults to 2.
    /// </summary>
    public int MaxTurnAttempts { get; init; } = 2;

    /// <summary>
    /// Gets an optional progress sink receiving stage transitions and per-chunk updates.
    /// </summary>
    public IProgress<MapReduceProgress>? Progress { get; init; }
}
