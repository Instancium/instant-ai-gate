namespace InstantAIGate.Core.Dtos.Status;

using InstantAIGate.Core.Dtos.Inference;
using System;
using System.Collections.Generic;

/// <summary>
/// Composite telemetry snapshot broadcast to the dashboard at 1 Hz.
/// Aggregates gateway status, inference metrics, hardware state, model pools, and sessions.
/// </summary>
public sealed record DashboardTelemetrySnapshot
{
    public DateTimeOffset Timestamp { get; init; }
    public required GatewayStatusDetails GatewayStatus { get; init; }
    public required InferenceMetrics InferenceMetrics { get; init; }
    public required HardwareMetricsDto Hardware { get; init; }
    public IReadOnlyList<ModelPoolTelemetryDto> Models { get; init; } = [];
    public IReadOnlyList<SessionTelemetryDto> Sessions { get; init; } = [];
}
