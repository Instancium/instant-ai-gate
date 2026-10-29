namespace InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Configuration settings governing inference session lifecycle, idle evictions,
/// and connection drop grace periods.
/// </summary>
public sealed class SessionSettings
{
    public const string SectionName = "InstantAIGate:Sessions";

    /// <summary>
    /// Duration in seconds to retain a session and its native KV-cache in VRAM
    /// following a transport disconnection, allowing the client to reconnect seamlessly.
    /// Default is 30 seconds.
    /// </summary>
    public int DisconnectGracePeriodSeconds { get; set; } = 30;

    /// <summary>
    /// Inactivity timeout in minutes for connected sessions. If no user prompts
    /// or commands are received within this period, the session slot is returned to the pool.
    /// Default is 3 minutes.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 3;

    public TimeSpan DisconnectGracePeriod => TimeSpan.FromSeconds(DisconnectGracePeriodSeconds);

    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
}
