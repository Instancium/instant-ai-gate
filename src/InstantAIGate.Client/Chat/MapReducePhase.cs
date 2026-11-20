namespace InstantAIGate.Client.Chat;

/// <summary>
/// Identifies the current stage of a checkpointed Map-Reduce pipeline for progress reporting.
/// </summary>
public enum MapReducePhase
{
    /// <summary>The shared system prefix is being evaluated and pinned into the KV-cache.</summary>
    Warmup = 0,

    /// <summary>Independent chunks are being analyzed against the pinned warm prefix.</summary>
    Map = 1,

    /// <summary>Partial summaries are being hierarchically compressed to fit the context window.</summary>
    Reduce = 2,

    /// <summary>The final response is being generated from the aggregated summaries.</summary>
    Final = 3
}
