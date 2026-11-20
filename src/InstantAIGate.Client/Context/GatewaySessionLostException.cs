namespace InstantAIGate.Client.Context;

using System;

/// <summary>
/// Thrown when a checkpointed session can no longer guarantee the integrity of its pinned
/// KV-cache prefix (failed rollback, lost server-side session, or vanished warm-up tokens).
/// Callers must recreate the chat client and warm up the shared prefix again; continuing
/// on the same session would silently pollute the model context.
/// </summary>
public sealed class GatewaySessionLostException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GatewaySessionLostException"/> class.
    /// </summary>
    /// <param name="sessionId">The session whose pinned prefix was lost.</param>
    /// <param name="reason">A human-readable explanation of the integrity failure.</param>
    /// <param name="innerException">The underlying transport or gateway error, if any.</param>
    public GatewaySessionLostException(string sessionId, string reason, Exception? innerException = null)
        : base($"Gateway session '{sessionId}' lost its pinned KV-cache state: {reason}", innerException)
    {
        SessionId = sessionId;
    }

    /// <summary>
    /// Gets the identifier of the session whose KV-cache integrity was compromised.
    /// </summary>
    public string SessionId { get; }
}
