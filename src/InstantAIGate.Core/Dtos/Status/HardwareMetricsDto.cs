namespace InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Platform-level hardware and process memory metrics snapshot.
/// Populated by IHardwareTelemetryProvider (Stage 3).
/// </summary>
public sealed record HardwareMetricsDto
{
    public long ProcessRamBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long SystemTotalRamBytes { get; init; }
    public long SystemAvailableRamBytes { get; init; }
    public double CpuUsagePercent { get; init; }
    public long VramTotalBytes { get; init; }
    public long VramUsedBytes { get; init; }
    public long VramProcessUsedBytes { get; init; }
    public string GpuProviderName { get; init; } = "Unknown";
}
