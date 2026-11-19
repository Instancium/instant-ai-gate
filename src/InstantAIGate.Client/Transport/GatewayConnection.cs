namespace InstantAIGate.Client.Transport;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Thread-safe, lazy SignalR transport implementation for <see cref="IGatewayConnection"/>.
/// Handles Bearer token authentication, automatic reconnection, and gateway readiness validation.
/// </summary>
public sealed class GatewayConnection : IGatewayConnection
{
    private readonly ILogger<GatewayConnection> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private HubConnection? _hubConnection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayConnection"/> class.
    /// </summary>
    /// <param name="options">The configured gateway options.</param>
    /// <param name="logger">The logger used for transport lifecycle diagnostics.</param>
    public GatewayConnection(IOptions<GatewayOptions> options, ILogger<GatewayConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Options = options.Value ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
        _logger = logger;
    }

    /// <inheritdoc />
    public GatewayOptions Options { get; }

    /// <inheritdoc />
    public async Task<HubConnection> GetConnectedHubAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hubConnection is { State: HubConnectionState.Connected })
        {
            return _hubConnection;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_hubConnection is null)
            {
                _hubConnection = BuildHubConnection();
            }

            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                _logger.LogDebug("Starting SignalR connection to {HubUrl}.", Options.GetHubUrl());
                await _hubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Connected to InstantAIGate hub at {HubUrl}.", Options.GetHubUrl());
            }

            return _hubConnection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var hub = await GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        var status = await hub.InvokeAsync<GatewayStatusDetails>(
            GatewayHubMethods.GetGatewayStatus,
            cancellationToken).ConfigureAwait(false);

        if (status.Status != GatewayOperationalStatus.Ready)
        {
            throw new InvalidOperationException(
                $"InstantAIGate is not ready for inference. Current status: {status.Status}. " +
                $"Details: {status.StageDescription ?? status.ErrorMessage ?? "none"}.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_hubConnection is not null)
            {
                await _hubConnection.DisposeAsync().ConfigureAwait(false);
                _hubConnection = null;
            }
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }

    private HubConnection BuildHubConnection()
    {
        var hubUrl = Options.GetHubUrl();
        var delays = Options.ReconnectDelays.Length > 0
            ? Options.ReconnectDelays
            : [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(Options.ApiKey);
            })
            .WithAutomaticReconnect(delays)
            .Build();

        connection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "SignalR connection to {HubUrl} lost. Reconnecting.", hubUrl);
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR connection reestablished with ConnectionId {ConnectionId}.", connectionId);
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            if (error is not null)
            {
                _logger.LogError(error, "SignalR connection to {HubUrl} closed with error.", hubUrl);
            }
            else
            {
                _logger.LogInformation("SignalR connection to {HubUrl} closed.", hubUrl);
            }

            return Task.CompletedTask;
        };

        return connection;
    }
}
