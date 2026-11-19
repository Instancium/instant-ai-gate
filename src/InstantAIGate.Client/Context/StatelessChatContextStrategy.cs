namespace InstantAIGate.Client.Context;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Implements a zero-retention KV-cache strategy that joins a fresh session before each turn
/// and hard-destroys it immediately after the turn completes. Prevents cross-request state leakage
/// for single-shot prompts and independent batch requests.
/// </summary>
public sealed class StatelessChatContextStrategy : IChatContextStrategy
{
    /// <inheritdoc />
    public async Task PrepareTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await scope.Hub.InvokeAsync(
            GatewayHubMethods.JoinSession,
            scope.SessionId,
            scope.RepoId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SealTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await scope.Hub.InvokeAsync(
            GatewayHubMethods.DestroySession,
            scope.SessionId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CloseAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Task.CompletedTask;
    }
}
