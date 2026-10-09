namespace InstantAIGate.Core.Interfaces.Session;

/// <summary>
/// Provides a thread-safe registry for tracking ephemeral sessions bound to SignalR connection identifiers.
/// Ephemeral sessions must be forcibly destroyed when the underlying physical connection drops.
/// </summary>
public interface IEphemeralSessionRegistry
{
    /// <summary>
    /// Registers an ephemeral session for the specified connection identifier.
    /// </summary>
    /// <param name="connectionId">The SignalR connection identifier.</param>
    /// <param name="sessionId">The unique session identifier to track.</param>
    void RegisterEphemeralSession(string connectionId, string sessionId);

    /// <summary>
    /// Removes a specific session from the registry, typically called when a session is explicitly destroyed.
    /// </summary>
    /// <param name="sessionId">The unique session identifier to remove.</param>
    void UnregisterSession(string sessionId);

    /// <summary>
    /// Retrieves and removes all session identifiers associated with the disconnected connection.
    /// </summary>
    /// <param name="connectionId">The SignalR connection identifier that was disconnected.</param>
    /// <returns>A list of session identifiers that must be forcibly destroyed.</returns>
    IReadOnlyList<string> NotifyConnectionDisconnected(string connectionId);
}