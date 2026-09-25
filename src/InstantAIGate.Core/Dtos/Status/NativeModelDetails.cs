namespace InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Native backend details for a loaded model.
/// </summary>
public record NativeModelDetails
{
    /// <summary>
    /// Model repository identifier.
    /// </summary>
    public string RepoId { get; init; } = string.Empty;

    /// <summary>
    /// Context size for inference.
    /// </summary>
    public int ContextSize { get; init; }

    /// <summary>
    /// Number of GPU layers offloaded.
    /// </summary>
    public int GpuLayers { get; init; }

    /// <summary>
    /// Number of CPU threads used.
    /// </summary>
    public int Threads { get; init; }

    /// <summary>
    /// Whether flash attention is enabled.
    /// </summary>
    public bool FlashAttention { get; init; }

    /// <summary>
    /// Number of idle contexts in the pool.
    /// </summary>
    public int IdleContextsCount { get; init; }

    /// <summary>
    /// Backend type identifier.
    /// </summary>
    public string Backend { get; init; } = string.Empty;
}