namespace InstantAIGate.Client.Context;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Implements a multi-turn KV-cache strategy that joins the session once on the first turn,
/// retains the KV-cache across subsequent turns, and soft-releases the session on disposal.
/// Avoids re-evaluating prior conversation history on every turn.
/// </summary>
public sealed class ConversationalChatContextStrategy : IChatContextStrategy
{
    private bool _joined;

    /// <inheritdoc />
    public async Task PrepareTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (_joined)
        {
            return;
        }

        await scope.Hub.InvokeAsync(
            GatewayHubMethods.JoinSession,
            scope.SessionId,
            scope.RepoId,
            cancellationToken).ConfigureAwait(false);
        _joined = true;
    }

    /// <inheritdoc />
    public Task SealTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CloseAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!_joined)
        {
            return;
        }

        try
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.LeaveSession,
                scope.SessionId,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _joined = false;
        }
    }
}
