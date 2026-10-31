namespace InstantAIGate.Core.Dtos.Infrastructure;

/// <summary>
/// Immutable snapshot produced by a platform VRAM probe.
/// Declared as a readonly record struct to stay allocation-free on the 1 Hz poll path.
/// </summary>
public readonly record struct VramProbeResult(
    long TotalBytes,
    long UsedBytes,
    long ProcessUsedBytes,
    string ProviderName,
    bool IsGpuPresent)
{
    public const string FallbackProviderName = "CPU / Unified Memory";

    /// <summary>
    /// Result reported when no GPU metadata is reachable (CPU-only host,
    /// container without GPU passthrough, probe failure).
    /// </summary>
    public static VramProbeResult Unavailable { get; } =
        new(0, 0, 0, FallbackProviderName, false);
}
