namespace InstantAIGate.Server.Configuration;

using System;

/// <summary>
/// Configuration for the SignalR gateway transport layer.
///
/// The effective receive size is always clamped between <see cref="DefaultReceiveMessageBytes"/>
/// and <see cref="HardReceiveMessageCeilingBytes"/> to preserve the memory-guard property.
/// An operator can raise the limit for multimodal payloads but can never disable the guard.
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
    /// 16 MiB is a pragmatic upper bound for base64 multimodal payloads.
    /// </summary>
    public const int HardReceiveMessageCeilingBytes = 16_777_216;

    /// <summary>
    /// Requested maximum inbound message size in bytes.
    /// Values at or below zero mean "use the default".
    /// </summary>
    public int MaximumReceiveMessageBytes { get; init; }

    /// <summary>
    /// Propagates server-side exception details to the calling client.
    ///
    /// WHY DEFAULT TRUE: with the framework default (false) every failed admin RPC surfaces
    /// as "Failed to invoke 'X' due to an error on the server." — the dashboard operation
    /// journal then records a failure with no actionable cause, which is indistinguishable
    /// from a dead control. The gateway is an operator-facing, key-authenticated surface,
    /// so diagnostic transparency outweighs message-hiding here.
    /// </summary>
    public bool EnableDetailedErrors { get; init; } = true;

    /// <summary>
    /// Computes the effective maximum receive message size, clamped to
    /// [DefaultReceiveMessageBytes, HardReceiveMessageCeilingBytes].
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