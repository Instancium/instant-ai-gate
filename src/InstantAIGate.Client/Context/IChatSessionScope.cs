namespace InstantAIGate.Client.Context;

using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Represents the active session binding passed to an <see cref="IChatContextStrategy"/>
/// during turn preparation, sealing, and disposal. Encapsulates session identity and transport
/// handle so strategies remain stateless or self-contained with respect to hub mechanics.
/// </summary>
public interface IChatSessionScope
{
    /// <summary>
    /// Gets the unique session identifier tracked by the gateway KV-cache pool.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Gets the target model repository identifier, or empty string to use the gateway's primary model.
    /// </summary>
    string RepoId { get; }

    /// <summary>
    /// Gets the connected SignalR <see cref="HubConnection"/> used to dispatch session lifecycle commands.
    /// </summary>
    HubConnection Hub { get; }
}
