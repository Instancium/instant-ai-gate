namespace InstantAIGate.Client.Context;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Extension point for KV-cache lifecycle policies in <see cref="Chat.GatewayChatClient"/>.
/// Separating session lifecycle hooks from streaming transport allows callers to introduce
/// custom KV-cache retention, rollback, or eviction behaviors without modifying the chat client.
/// </summary>
public interface IChatContextStrategy
{
    /// <summary>
    /// Prepares the remote gateway session before a prompt delta is dispatched.
    /// </summary>
    /// <param name="scope">The active session scope.</param>
    /// <param name="cancellationToken">A token to cancel the preparation call.</param>
    Task PrepareTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes or cleans up the remote gateway session after a turn completes or fails.
    /// </summary>
    /// <param name="scope">The active session scope.</param>
    /// <param name="cancellationToken">A token to cancel the sealing call.</param>
    Task SealTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases any retained remote session resources when the owning chat client is disposed.
    /// </summary>
    /// <param name="scope">The active session scope.</param>
    /// <param name="cancellationToken">A token to cancel the close call.</param>
    Task CloseAsync(IChatSessionScope scope, CancellationToken cancellationToken = default);
}
