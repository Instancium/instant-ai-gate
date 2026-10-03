namespace InstantAIGate.Core.Interfaces.Inference;

using System.Threading.Channels;

/// <summary>
/// Defines a contract for signaling and consuming system telemetry and queue state mutations.
/// </summary>
public interface IMetricsEventSource
{
    /// <summary>
    /// Gets the channel reader that emits signals whenever system telemetry state changes.
    /// </summary>
    ChannelReader<bool> Reader { get; }

    /// <summary>
    /// Signals that a telemetry state mutation (leases, queue, or model configuration) has occurred.
    /// </summary>
    void NotifyStateChanged();
}