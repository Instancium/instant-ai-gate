namespace InstantAIGate.Core.Dtos.Status;

using InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Immutable snapshot of a loaded model's pool and slot state for dashboard consumption.
/// </summary>
public sealed record ModelPoolTelemetryDto
{
    public required string RepoId { get; init; }
    public ModelType Type { get; init; }
    public bool IsLoaded { get; init; }
    public bool IsDraining { get; init; }
    public int ActiveLeases { get; init; }
    public int IdlePooledContexts { get; init; }
    public int MaxContexts { get; init; }
    public int GpuLayers { get; init; }
    public int TotalLayers { get; init; }
    public int ContextSize { get; init; }
    public string KvCacheQuantization { get; init; } = "F16";
    public long ModelWeightsBytes { get; init; }
}
