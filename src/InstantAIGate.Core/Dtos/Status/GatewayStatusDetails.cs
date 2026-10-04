namespace InstantAIGate.Core.Dtos.Status;

using System;

/// <summary>
/// Immutable snapshot representing the detailed operational and telemetry state of the gateway.
/// </summary>
public sealed record GatewayStatusDetails
{
    public GatewayOperationalStatus Status { get; init; } = GatewayOperationalStatus.Uninitialized;

    public string? ActiveModelId { get; init; }

    public string? StageDescription { get; init; }

    public float ProgressPercentage { get; init; }

    public long DownloadedBytes { get; init; }

    public long TotalBytes { get; init; }

    public double BytesPerSecond { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}