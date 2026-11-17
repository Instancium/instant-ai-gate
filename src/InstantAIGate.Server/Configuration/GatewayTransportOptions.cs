namespace InstantAIGate.Server.Configuration;

using System;

/// <summary>
/// Configuration for the SignalR gateway transport layer.
/// Controls the maximum inbound message size accepted by the hub.
///
/// The effective value is always clamped between <see cref="DefaultReceiveMessageBytes"/>
/// and <see cref="HardReceiveMessageCeilingBytes"/> to preserve the memory-guard
/// property of the original SignalR limit. An operator can raise the limit for
/// multimodal payloads but can never disable the guard entirely.
/// </summary>
public sealed record GatewayTransportOptions
{
    /// <summary>
    /// Default inbound message size limit (32 KB), matching the SignalR framework default.
    /// This is the floor: values below it are silently raised.
    /// </summary>
    public const int DefaultReceiveMessageBytes = 32_768;

    /// <summary>
    /// Hard ceiling that cannot be exceeded regardless of configuration.
    /// 16 MiB is chosen as a pragmatic upper bound for base64-encoded
    /// multimodal payloads within a 4096-token context window.
    /// </summary>
    public const int HardReceiveMessageCeilingBytes = 16_777_216;

    /// <summary>
    /// Requested maximum inbound message size in bytes.
    /// Values at or below zero are treated as "use the default".
    /// Values below the default floor are clamped up.
    /// Values above the hard ceiling are clamped down.
    /// </summary>
    public int MaximumReceiveMessageBytes { get; init; }

    /// <summary>
    /// Computes the effective maximum receive message size,
    /// clamped to [DefaultReceiveMessageBytes, HardReceiveMessageCeilingBytes].
    /// Pure function, safe to call repeatedly.
    /// </summary>
    public int GetEffectiveReceiveMessageSize()
    {
        int requested = MaximumReceiveMessageBytes > 0
            ? MaximumReceiveMessageBytes
            : DefaultReceiveMessageBytes;
        return Math.Clamp(requested, DefaultReceiveMessageBytes, HardReceiveMessageCeilingBytes);
    }
}
