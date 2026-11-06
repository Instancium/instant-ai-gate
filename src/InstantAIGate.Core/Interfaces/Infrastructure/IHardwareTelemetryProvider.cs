namespace InstantAIGate.Core.Interfaces.Infrastructure;

using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Vendor-agnostic collector of host, process and GPU memory telemetry.
/// Implementations must never throw: any unavailable source degrades to a
/// zero-valued field with a descriptive provider name.
/// </summary>
/// <remarks>
/// A single capture is expected to complete within ~15 ms so it can be polled
/// at 1 Hz by the dashboard broadcaster without affecting inference latency.
/// </remarks>
public interface IHardwareTelemetryProvider
{
    HardwareMetricsDto CaptureMetrics();
}
