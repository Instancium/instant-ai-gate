namespace InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Represents the operational state of an inference session slot.
/// </summary>
public enum SessionStateType
{
    /// <summary>Session is idle with a bound or unbound connection, no active generation.</summary>
    Idle = 0,

    /// <summary>Session is actively generating tokens (ExecutionGate is held).</summary>
    Generating = 1,

    /// <summary>Connection was lost; session is in grace period before VRAM eviction.</summary>
    DisconnectedGracePeriod = 2
}
