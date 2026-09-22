namespace InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Configuration for model loading and inference parameters.
/// </summary>
public record ModelSettings
{
    /// <summary>
    /// Model repository identifier.
    /// </summary>
    public string RepoId { get; init; } = string.Empty;

    /// <summary>
    /// Number of layers to offload to GPU.
    /// </summary>
    public int GpuLayerCount { get; init; }

    /// <summary>
    /// Main GPU device index.
    /// </summary>
    public int MainGPU { get; init; }

    /// <summary>
    /// Context size for inference.
    /// </summary>
    public int ContextSize { get; init; } = 2048;

    /// <summary>
    /// Batch size for inference.
    /// </summary>
    public int BatchSize { get; init; } = 512;

    /// <summary>
    /// Number of CPU threads for inference.
    /// </summary>
    public int Threads { get; init; }

    /// <summary>
    /// Whether to enable flash attention.
    /// </summary>
    public bool FlashAttention { get; init; }

    /// <summary>
    /// Whether to enable embeddings mode.
    /// </summary>
    public bool Embeddings { get; init; }

    /// <summary>
    /// KV cache quantization type.
    /// </summary>
    public string KvCacheQuantization { get; init; } = "F16";

    /// <summary>
    /// Whether to lock model memory.
    /// </summary>
    public bool UseMemoryLock { get; init; }

    /// <summary>
    /// Whether the model supports vision/multimodal inference.
    /// </summary>
    public bool VisionSupport { get; init; }

    /// <summary>
    /// Maximum number of concurrent contexts.
    /// </summary>
    public int MaxContexts { get; init; } = 4;

    /// <summary>
    /// Maximum allowed model file size in megabytes.
    /// </summary>
    public long MaxModelFileSizeMb { get; init; } = 50000;

    /// <summary>
    /// Model type classification.
    /// </summary>
    public ModelType Type { get; init; } = ModelType.Llm;
}