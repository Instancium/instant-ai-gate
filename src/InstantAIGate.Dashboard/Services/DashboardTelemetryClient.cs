namespace InstantAIGate.Dashboard.Services;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Scoped SignalR client for the diagnostic dashboard.
/// Owns the admin connection lifecycle, the 1 Hz telemetry ring buffers and all
/// control-plane invocations. Every invoke is bounded by <see cref="DefaultInvokeTimeout"/>
/// so a hanging server-side drain can never freeze the UI indefinitely.
///
/// AUTHENTICATION: the API key is optional. When absent the client connects without a
/// bearer token and lets the server decide; a server with trusted-loopback enabled
/// authenticates local callers as Admin with zero configuration, while a remote server
/// rejects the handshake and the dashboard falls back to the key-entry gate.
/// </summary>
public sealed class DashboardTelemetryClient : IAsyncDisposable
{
    private const int DefaultHistoryCapacity = 120;
    private const int DefaultLogCapacity = 400;

    /// <summary>
    /// Upper bound for a single control-plane RPC. Chosen above the worst observed
    /// graceful drain while far below "user believes the button is dead".
    /// </summary>
    public static readonly TimeSpan DefaultInvokeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Hub route relative to the host root. Must be resolved to an absolute URI in the
    /// composition root: the Blazor WebAssembly HTTP stack resolves relative URIs against
    /// the runtime virtual file system root, not against the document base address.
    /// </summary>
    public const string HubRelativePath = "hub/gateway";

    private static readonly object?[] NoArguments = Array.Empty<object?>();

    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private readonly IAdminApiKeyStore _apiKeyStore;
    private readonly ILogger<DashboardTelemetryClient> _logger;
    private readonly Uri _hubUrl;
    private readonly Action<HttpConnectionOptions>? _configureConnection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private HubConnection? _connection;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    public CircularBuffer<DashboardTelemetrySnapshot> History { get; } = new(DefaultHistoryCapacity);
    public CircularBuffer<LogEntry> Logs { get; } = new(DefaultLogCapacity);

    public ConnectionState State => _state;

    public event Action<DashboardTelemetrySnapshot>? DashboardTelemetryReceived;
    public event Action<GatewayStatusDetails>? GatewayStatusChanged;
    public event Action<DownloadProgress>? DownloadProgressChanged;
    public event Action<string, string, string>? LogReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    /// <param name="apiKeyStore">Browser-scoped admin key storage (may be empty for loopback trust).</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <param name="hubUrl">Absolute hub URI resolved in the composition root.</param>
    /// <param name="configureConnection">
    /// Optional transport seam applied after the access-token provider is wired.
    /// Exists so integration tests can route the real client through an in-memory
    /// TestServer handler; the production composition root leaves it null.
    /// </param>
    public DashboardTelemetryClient(
        IAdminApiKeyStore apiKeyStore,
        ILogger<DashboardTelemetryClient> logger,
        Uri hubUrl,
        Action<HttpConnectionOptions>? configureConnection = null)
    {
        _apiKeyStore = apiKeyStore ?? throw new ArgumentNullException(nameof(apiKeyStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(hubUrl);

        if (!hubUrl.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "Hub URL must be absolute. Resolve it against IWebAssemblyHostEnvironment.BaseAddress in the composition root.",
                nameof(hubUrl));
        }

        _hubUrl = hubUrl;
        _configureConnection = configureConnection;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                return;
            }

            string? apiKey = await _apiKeyStore.GetApiKeyAsync(ct);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Admin API key is not configured in browser storage.");
            }

            await DisposeConnectionInternalAsync();

            var builder = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(apiKey);
                    _configureConnection?.Invoke(options);
                })
                .WithAutomaticReconnect(RetryBackoff);

            HubConnection connection = builder.Build();
            _connection = connection;

            WireHandlers(connection);
            WireLifecycleHooks(connection);

            SetState(ConnectionState.Connecting);

            await connection.StartAsync(ct);
            await InvokeWithTimeoutAsync(
                token2 => connection.InvokeCoreAsync("SubscribeToDashboardTelemetry", NoArguments, token2),
                ct);

            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            await DisposeConnectionInternalAsync();
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                try
                {
                    await _connection.InvokeCoreAsync("UnsubscribeFromDashboardTelemetry", NoArguments, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to unsubscribe before disconnect.");
                }

                await DisposeConnectionInternalAsync();
            }

            SetState(ConnectionState.Disconnected);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<DashboardTelemetrySnapshot?> GetSnapshotAsync(CancellationToken ct = default)
    {
        HubConnection? connection = _connection;
        if (connection?.State != HubConnectionState.Connected)
        {
            return null;
        }

        return await InvokeWithTimeoutAsync(
            token => connection.InvokeCoreAsync<DashboardTelemetrySnapshot>(
                "GetDashboardSnapshotAsync", NoArguments, token),
            ct);
    }

    public Task ForceKillSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return InvokeAdminAsync("ForceKillSessionAsync", ct, sessionId);
    }

    public Task PurgeIdleContextsAsync(string repoId, CancellationToken ct = default)
    {
        return InvokeAdminAsync("PurgeIdleContextsAsync", ct, repoId ?? string.Empty);
    }

    public Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        return InvokeAdminAsync("UnloadModelAsync", ct, repoId);
    }

    public Task SetQueueLimitAsync(int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Queue limit must be greater than zero.");
        }

        return InvokeAdminAsync("SetQueueLimitAsync", ct, limit);
    }

    public Task TriggerGarbageCollectionAsync(CancellationToken ct = default)
    {
        return InvokeAdminAsync("TriggerGarbageCollectionAsync", ct);
    }

    /// <summary>
    /// Issues a control-plane RPC with a positional argument array.
    /// InvokeCoreAsync is array-native: the InvokeAsync extension set would ship the whole
    /// array as ONE JSON-array argument, which the server cannot bind to scalar parameters.
    /// The array is forwarded exactly as declared (no null filtering, which would shift positions).
    /// </summary>
    private Task InvokeAdminAsync(string method, CancellationToken ct, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HubConnection connection = _connection
            ?? throw new InvalidOperationException(
                "Dashboard telemetry client is not connected. Reconnect before issuing admin commands.");

        if (connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "Dashboard telemetry client is not connected. Reconnect before issuing admin commands.");
        }

        object?[] payload = args ?? NoArguments;

        return InvokeWithTimeoutAsync(
            token => connection.InvokeCoreAsync(method, payload, token),
            ct);
    }

    /// <summary>
    /// Bounds every RPC with <see cref="DefaultInvokeTimeout"/> linked to the caller token.
    /// Server-side admin operations may block on native drains; without a client bound
    /// the awaiting button spinner would spin forever and look like a dead control.
    /// </summary>
    private async Task InvokeWithTimeoutAsync(Func<CancellationToken, Task> invoke, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(DefaultInvokeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await invoke(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Gateway did not respond within {DefaultInvokeTimeout.TotalSeconds:F0} s. The operation may still be running server-side.");
        }
    }

    private async Task<T> InvokeWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> invoke, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(DefaultInvokeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await invoke(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Gateway did not respond within {DefaultInvokeTimeout.TotalSeconds:F0} s. The operation may still be running server-side.");
        }
    }

    private void WireHandlers(HubConnection connection)
    {
        connection.On<DashboardTelemetrySnapshot>("ReceiveDashboardTelemetry", snapshot =>
        {
            if (snapshot is null)
            {
                return;
            }

            History.Add(snapshot);
            DashboardTelemetryReceived?.Invoke(snapshot);
        });

        connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status =>
        {
            if (status is not null)
            {
                GatewayStatusChanged?.Invoke(status);
            }
        });

        connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
        {
            if (progress is not null)
            {
                DownloadProgressChanged?.Invoke(progress);
            }
        });

        connection.On<string, string, string>("ReceiveLog", (level, category, message) =>
        {
            string lvl = level ?? "Information";
            string cat = category ?? string.Empty;
            string msg = message ?? string.Empty;

            Logs.Add(new LogEntry(DateTimeOffset.UtcNow, lvl, cat, msg));
            LogReceived?.Invoke(lvl, cat, msg);
        });
    }

    private void WireLifecycleHooks(HubConnection connection)
    {
        connection.Reconnecting += _ =>
        {
            SetState(ConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            _logger.LogInformation("SignalR reconnected with id {ConnectionId}. Resubscribing.", connectionId);
            SetState(ConnectionState.Connected);

            try
            {
                await connection.InvokeCoreAsync("SubscribeToDashboardTelemetry", NoArguments, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resubscribe after reconnect.");
            }
        };

        connection.Closed += _ =>
        {
            SetState(ConnectionState.Disconnected);
            return Task.CompletedTask;
        };
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(state);
    }

    private async Task DisposeConnectionInternalAsync()
    {
        if (_connection is null)
        {
            return;
        }

        HubConnection connection = _connection;
        _connection = null;

        try
        {
            await connection.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping SignalR connection.");
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing SignalR connection.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _connectLock.WaitAsync();
        try
        {
            await DisposeConnectionInternalAsync();
        }
        finally
        {
            _connectLock.Release();
            _connectLock.Dispose();
        }
    }
}