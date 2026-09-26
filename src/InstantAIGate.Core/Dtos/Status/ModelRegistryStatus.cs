

using InstantAIGate.Core.Dtos.Config;

namespace InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Status information for a model in the registry.
/// </summary>
public record ModelRegistryStatus
{
    /// <summary>
    /// Model repository identifier.
    /// </summary>
    public string RepoId { get; init; }

    /// <summary>
    /// Whether the model is currently loaded.
    /// </summary>
    public bool IsLoaded { get; init; }

    /// <summary>
    /// Number of idle contexts in the pool.
    /// </summary>
    public int IdleContextsCount { get; init; }

    /// <summary>
    /// Maximum number of contexts allowed.
    /// </summary>
    public int MaxContexts { get; init; }

    /// <summary>
    /// Number of GPU layers offloaded.
    /// </summary>
    public int GpuLayers { get; init; }

    /// <summary>
    /// Model type classification.
    /// </summary>
    public ModelType Type { get; init; }

    /// <summary>
    /// Initializes a new instance of the model registry status.
    /// </summary>
    public ModelRegistryStatus(
        string repoId,
        bool isLoaded,
        int idleContextsCount,
        int maxContexts,
        int gpuLayers,
        ModelType type)
    {
        RepoId = repoId;
        IsLoaded = isLoaded;
        IdleContextsCount = idleContextsCount;
        MaxContexts = maxContexts;
        GpuLayers = gpuLayers;
        Type = type;
    }
}