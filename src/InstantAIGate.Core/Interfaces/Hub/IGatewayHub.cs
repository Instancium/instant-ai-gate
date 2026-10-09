using System.Threading.Tasks;

namespace InstantAIGate.Core.Interfaces.Hub;

/// <summary>
/// Defines the server-side hub contract exposed to remote clients.
/// </summary>
public interface IGatewayHub
{
    /// <summary>
    /// Marks the specified session as ephemeral, binding its lifecycle to the current physical connection.
    /// If the connection drops, the server guarantees deterministic VRAM slot destruction for this session.
    /// </summary>
    /// <param name="sessionId">The session identifier to mark as ephemeral.</param>
    /// <returns>A task representing the asynchronous registration operation.</returns>
    Task MarkSessionAsEphemeral(string sessionId);
}