namespace InstantAIGate.Client.Context;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Implements a prefix-preserving KV-cache strategy that retains the session across turns,
/// records a checkpoint token position via <c>GetSessionTokenCount</c>, and rolls the KV-cache
/// back to that pinned prefix after each turn. Ideal for evaluating multiple independent queries
/// against a shared warm system or document prefix.
/// </summary>
public sealed class CheckpointedChatContextStrategy : IChatContextStrategy
{
    private bool _joined;
    private int? _checkpointPosition;

    /// <summary>
    /// Gets the currently pinned KV-cache token position, or <c>null</c> if no checkpoint has been recorded yet.
    /// </summary>
    public int? CheckpointPosition => _checkpointPosition;

    /// <summary>
    /// Explicitly queries the gateway for the current session token count and pins it as the rollback target
    /// for subsequent turns. Use this after warming up a shared prefix.
    /// </summary>
    /// <param name="scope">The active session scope.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The recorded checkpoint token count.</returns>
    public async Task<int> CaptureCheckpointAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var count = await scope.Hub.InvokeAsync<int>(
            GatewayHubMethods.GetSessionTokenCount,
            scope.SessionId,
            cancellationToken).ConfigureAwait(false);

        _checkpointPosition = count;
        return count;
    }

    /// <inheritdoc />
    public async Task PrepareTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!_joined)
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.JoinSession,
                scope.SessionId,
                scope.RepoId,
                cancellationToken).ConfigureAwait(false);
            _joined = true;
        }

        if (_checkpointPosition is null)
        {
            await CaptureCheckpointAsync(scope, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SealTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (_joined && _checkpointPosition is int targetPosition)
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.RollbackSession,
                scope.SessionId,
                targetPosition,
                cancellationToken).ConfigureAwait(false);
        }
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
            _checkpointPosition = null;
        }
    }
}
