namespace InstantAIGate.Dashboard.Services;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

public sealed class DashboardTelemetryClient : IAsyncDisposable
{
    private const int DefaultHistoryCapacity = 120;

    /// <summary>
    /// Hub route relative to the host root. Must be resolved to an absolute URI in the
    /// composition root: the Blazor WebAssembly HTTP stack resolves relative URIs against
    /// the runtime virtual file system root (file:///), not against the document base address.
    /// </summary>
    public const string HubRelativePath = "hub/gateway";

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
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private HubConnection? _connection;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    public CircularBuffer<DashboardTelemetrySnapshot> History { get; } = new(DefaultHistoryCapacity);
    public ConnectionState State => _state;

    public event Action<DashboardTelemetrySnapshot>? DashboardTelemetryReceived;
    public event Action<GatewayStatusDetails>? GatewayStatusChanged;
    public event Action<DownloadProgress>? DownloadProgressChanged;
    public event Action<string, string, string>? LogReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public DashboardTelemetryClient(
        IAdminApiKeyStore apiKeyStore,
        ILogger<DashboardTelemetryClient> logger,
        Uri hubUrl)
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
                    options.AccessTokenProvider = () => Task.FromResult<string?>(apiKey);
                })
                .WithAutomaticReconnect(RetryBackoff);

            _connection = builder.Build();
            WireHandlers(_connection);
            WireLifecycleHooks(_connection);

            SetState(ConnectionState.Connecting);
            await _connection.StartAsync(ct);
            await _connection.InvokeAsync("SubscribeToDashboardTelemetry", ct);
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
                    await _connection.InvokeAsync("UnsubscribeFromDashboardTelemetry", ct);
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
        if (_connection?.State != HubConnectionState.Connected)
        {
            return null;
        }

        return await _connection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync", ct);
    }

    public async Task ForceKillSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await InvokeAdminAsync("ForceKillSessionAsync", ct, sessionId);
    }

    public async Task PurgeIdleContextsAsync(string repoId, CancellationToken ct = default)
    {
        await InvokeAdminAsync("PurgeIdleContextsAsync", ct, repoId ?? string.Empty);
    }

    public async Task TriggerGarbageCollectionAsync(CancellationToken ct = default)
    {
        await InvokeAdminAsync("TriggerGarbageCollectionAsync", ct);
    }

    private async Task InvokeAdminAsync(string method, CancellationToken ct, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Dashboard telemetry client is not connected.");
        }

        object[] payload = args.Where(a => a is not null).Cast<object>().ToArray();
        await _connection.InvokeAsync(method, payload, ct);
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
            LogReceived?.Invoke(level ?? string.Empty, category ?? string.Empty, message ?? string.Empty);
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
                await connection.InvokeAsync("SubscribeToDashboardTelemetry");
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