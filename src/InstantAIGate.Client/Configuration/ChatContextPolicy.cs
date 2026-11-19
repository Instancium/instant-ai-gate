namespace InstantAIGate.Client.Configuration;

/// <summary>
/// Defines built-in KV-cache session lifecycle policies for chat clients.
/// Selecting an explicit policy decouples callers from manual session join, cleanup,
/// and context retention mechanics.
/// </summary>
public enum ChatContextPolicy
{
    /// <summary>
    /// Isolates every inference request in a dedicated session that is destroyed immediately
    /// after completion, preventing cross-request KV-cache leakage.
    /// </summary>
    Stateless = 0,

    /// <summary>
    /// Retains the active session and its KV-cache across turns within the client scope
    /// until the client is disposed, enabling multi-turn conversations without re-evaluating history.
    /// </summary>
    Conversational = 1
}
