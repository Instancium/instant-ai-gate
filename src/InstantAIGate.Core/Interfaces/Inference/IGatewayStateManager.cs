namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Status;
using System;

/// <summary>
/// Contract for managing and observing the operational lifecycle state of the AI Gateway.
/// </summary>
public interface IGatewayStateManager
{
    /// <summary>
    /// Raised whenever the operational status or telemetry metrics change.
    /// Handlers are invoked asynchronously/isolated outside the state mutation lock.
    /// </summary>
    event Action<GatewayStatusDetails>? StatusChanged;

    /// <summary>
    /// Retrieves an immutable point-in-time snapshot of the current gateway operational state.
    /// </summary>
    GatewayStatusDetails GetSnapshot();

    /// <summary>
    /// Transitions state to downloading and updates real-time network transfer telemetry.
    /// </summary>
    void SetDownloading(
        string modelId,
        float progressPercentage,
        long downloadedBytes,
        long totalBytes,
        double bytesPerSecond,
        string? stageDescription = null);

    /// <summary>
    /// Transitions state to native memory/VRAM allocation and context loading.
    /// </summary>
    void SetLoading(string modelId, string? stageDescription = null);

    /// <summary>
    /// Transitions state to operational readiness for serving inference requests.
    /// </summary>
    void SetReady(string modelId, string? stageDescription = null);

    /// <summary>
    /// Transitions state to a faulted condition with descriptive diagnostic context.
    /// </summary>
    void SetFaulted(string? modelId, string errorMessage, Exception? exception = null);

    /// <summary>
    /// Resets the state back to uninitialized.
    /// </summary>
    void Reset();
}