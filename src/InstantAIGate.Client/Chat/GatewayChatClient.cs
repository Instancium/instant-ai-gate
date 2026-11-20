namespace InstantAIGate.Client.Chat;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InstantAIGate.Client.Context;
using InstantAIGate.Client.Transport;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

/// <summary>
/// SignalR-backed <see cref="IChatClient"/> implementation that delegates KV-cache session
/// lifecycle management to an <see cref="IChatContextStrategy"/> while streaming token deltas
/// through an unbounded <see cref="Channel{T}"/>.
/// </summary>
public sealed class GatewayChatClient : IChatClient
{
    private readonly IGatewayConnection _connection;
    private readonly IChatContextStrategy _strategy;
    private readonly ILogger<GatewayChatClient> _logger;
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private HubConnection? _lastHub;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayChatClient"/> class.
    /// </summary>
    /// <param name="connection">The shared SignalR gateway connection.</param>
    /// <param name="strategy">The KV-cache context strategy governing session lifecycle.</param>
    /// <param name="logger">The logger for chat transport diagnostics.</param>
    public GatewayChatClient(
        IGatewayConnection connection,
        IChatContextStrategy strategy,
        ILogger<GatewayChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(logger);

        _connection = connection;
        _strategy = strategy;
        _logger = logger;

        var prefix = string.IsNullOrWhiteSpace(connection.Options.ClientId)
            ? "client"
            : connection.Options.ClientId.Trim();
        SessionId = $"{prefix}-{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public string SessionId { get; }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var builder = new StringBuilder();
        await StreamAsync(
            message,
            token =>
            {
                builder.Append(token);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task StreamAsync(
        ChatMessage message,
        Func<string, Task> onToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(onToken);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _turnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _connection.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
            _lastHub = hub;

            var scope = new ChatSessionScope(SessionId, _connection.Options.LlmRepoId, hub);
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            using var tokenSubscription = hub.On<SessionTokenDelta>(
                GatewayHubMethods.ReceiveTokenDelta,
                delta =>
                {
                    if (!string.Equals(delta.SessionId, SessionId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        channel.Writer.TryWrite(delta.Content);
                    }

                    if (delta.IsDone)
                    {
                        channel.Writer.TryComplete();
                    }
                });

            using var errorSubscription = hub.On<string>(
                GatewayHubMethods.ReceiveError,
                error =>
                {
                    channel.Writer.TryComplete(new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "Gateway returned an unspecified inference error." : error));
                });

            using var overflowSubscription = hub.On<ContextOverflowReport>(
                GatewayHubMethods.ReceiveContextOverflow,
                report =>
                {
                    if (!string.Equals(report.SessionId, SessionId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    channel.Writer.TryComplete(report.ToException());
                });

            using var sessionClosedSubscription = hub.On<string>(
                GatewayHubMethods.ReceiveSessionClosed,
                sessionId =>
                {
                    if (!string.Equals(sessionId, SessionId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    channel.Writer.TryComplete(new IOException(
                        $"Gateway closed session '{SessionId}' while inference was in flight. " +
                        "The response may be incomplete."));
                });

            Task OnClosed(Exception? error)
            {
                var ex = error ?? new IOException("SignalR connection closed while streaming inference tokens.");
                channel.Writer.TryComplete(ex);
                return Task.CompletedTask;
            }

            Task OnReconnected(string? _)
            {
                channel.Writer.TryComplete(
                    new IOException("SignalR connection reconnected mid-stream; in-flight inference was interrupted."));
                return Task.CompletedTask;
            }

            hub.Closed += OnClosed;
            hub.Reconnected += OnReconnected;

            try
            {
                await _strategy.PrepareTurnAsync(scope, cancellationToken).ConfigureAwait(false);

                await hub.InvokeAsync(
                    GatewayHubMethods.SendPromptDelta,
                    SessionId,
                    message,
                    cancellationToken).ConfigureAwait(false);

                await foreach (var token in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await onToken(token).ConfigureAwait(false);
                }
            }
            finally
            {
                hub.Closed -= OnClosed;
                hub.Reconnected -= OnReconnected;
                channel.Writer.TryComplete();

                try
                {
                    await _strategy.SealTurnAsync(scope, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to seal session {SessionId} during cleanup.", SessionId);
                }
            }
        }
        finally
        {
            _turnLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _turnLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_lastHub is { State: HubConnectionState.Connected })
            {
                var scope = new ChatSessionScope(SessionId, _connection.Options.LlmRepoId, _lastHub);
                try
                {
                    await _strategy.CloseAsync(scope, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close session {SessionId} during disposal.", SessionId);
                }
            }
        }
        finally
        {
            _turnLock.Release();
            _turnLock.Dispose();
        }
    }

    private sealed record ChatSessionScope(string SessionId, string RepoId, HubConnection Hub) : IChatSessionScope;
}
