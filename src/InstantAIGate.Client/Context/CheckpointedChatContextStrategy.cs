namespace InstantAIGate.Client.Context;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Prefix-preserving KV-cache strategy for checkpointed fan-out workloads (Map-Reduce and similar).
/// Semantics: the first turn is the shared warm-up prefix and is pinned at its end position when
/// the turn seals; every subsequent turn is rolled back to that pinned position, so independent
/// queries reuse the warm prefix without cross-contamination.
/// <para>
/// INTEGRITY VERIFICATION: GatewayHub reports JoinSession/RollbackSession failures through the
/// ReceiveError client callback instead of faulting the hub invocation, so a successful InvokeAsync
/// is NOT proof of success. This strategy therefore verifies every lifecycle mutation by reading
/// GetSessionTokenCount afterwards: the warm-up must have cached a positive token count, and a
/// rollback must leave exactly the pinned count. Any mismatch marks the strategy as faulted,
/// forcing callers to recreate the client and re-warm the prefix rather than silently continuing
/// on a polluted KV-cache.
/// </para>
/// <para>
/// Reconnect-aware: re-joins when the hub ConnectionId changes (server-side sessions survive
/// reconnects within the disconnect grace period) and verifies the pinned prefix still exists.
/// Not thread-safe: owned by a single chat client that serializes turns.
/// </para>
/// </summary>
public sealed class CheckpointedChatContextStrategy : IChatContextStrategy
{
    private string? _joinedConnectionId;
    private int? _checkpointPosition;
    private bool _faulted;

    /// <summary>
    /// Gets the currently pinned KV-cache token position, or <c>null</c> before the warm-up
    /// turn has been sealed or an explicit checkpoint has been captured.
    /// </summary>
    public int? CheckpointPosition => _checkpointPosition;

    /// <summary>
    /// Explicitly queries the gateway for the current session token count and pins it as the
    /// rollback target for subsequent turns. Use this to re-pin after an additional warm-up
    /// message; the first turn is pinned automatically and does not require this call.
    /// </summary>
    /// <param name="scope">The active session scope.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The recorded checkpoint token count.</returns>
    public async Task<int> CaptureCheckpointAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        int count = await scope.Hub.InvokeAsync<int>(
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
        if (_faulted)
        {
            throw new GatewaySessionLostException(
                scope.SessionId,
                "the pinned KV-cache prefix could not be restored on a previous turn. " +
                "Recreate the chat client and warm up the shared prefix again.");
        }

        string? connectionId = scope.Hub.ConnectionId;
        bool connectionChanged = _joinedConnectionId is null
            || !string.Equals(_joinedConnectionId, connectionId, StringComparison.Ordinal);

        if (connectionChanged)
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.JoinSession,
                scope.SessionId,
                scope.RepoId,
                cancellationToken).ConfigureAwait(false);
            _joinedConnectionId = connectionId;

            if (_checkpointPosition is int expected)
            {
                int actual = await scope.Hub.InvokeAsync<int>(
                    GatewayHubMethods.GetSessionTokenCount,
                    scope.SessionId,
                    cancellationToken).ConfigureAwait(false);
                if (actual < expected)
                {
                    _faulted = true;
                    throw new GatewaySessionLostException(
                        scope.SessionId,
                        $"the server retains {actual} tokens after reconnect, which is below the pinned checkpoint {expected}. " +
                        "The session was likely evicted after the disconnect grace period expired.");
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task SealTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_joinedConnectionId is null || _faulted)
        {
            return;
        }

        if (_checkpointPosition is null)
        {
            await SealWarmupTurnAsync(scope, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SealCheckpointedTurnAsync(scope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CloseAsync(IChatSessionScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_joinedConnectionId is null)
        {
            return;
        }

        try
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.DestroySession,
                scope.SessionId,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _joinedConnectionId = null;
            _checkpointPosition = null;
            _faulted = false;
        }
    }

    private async Task SealWarmupTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken)
    {
        int warmupTokens;
        try
        {
            warmupTokens = await scope.Hub.InvokeAsync<int>(
                GatewayHubMethods.GetSessionTokenCount,
                scope.SessionId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _faulted = true;
            throw new GatewaySessionLostException(
                scope.SessionId, "failed to read the warm-up token count.", ex);
        }

        if (warmupTokens <= 0)
        {
            _faulted = true;
            throw new GatewaySessionLostException(
                scope.SessionId,
                "the warm-up turn cached no tokens. The session join or prefix inference failed " +
                "server-side; the hub surfaces such failures via ReceiveError rather than faulting the invocation.");
        }

        _checkpointPosition = warmupTokens;
    }

    private async Task SealCheckpointedTurnAsync(IChatSessionScope scope, CancellationToken cancellationToken)
    {
        int target = _checkpointPosition!.Value;
        try
        {
            await scope.Hub.InvokeAsync(
                GatewayHubMethods.RollbackSession,
                scope.SessionId,
                target,
                cancellationToken).ConfigureAwait(false);

            int actual = await scope.Hub.InvokeAsync<int>(
                GatewayHubMethods.GetSessionTokenCount,
                scope.SessionId,
                cancellationToken).ConfigureAwait(false);
            if (actual != target)
            {
                _faulted = true;
                throw new GatewaySessionLostException(
                    scope.SessionId,
                    $"rollback verification failed: expected {target} cached tokens, but the gateway reports {actual}. " +
                    "The pinned prefix can no longer be guaranteed.");
            }
        }
        catch (GatewaySessionLostException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _faulted = true;
            throw new GatewaySessionLostException(
                scope.SessionId, $"rollback to token position {target} failed.", ex);
        }
    }
}