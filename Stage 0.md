Инкремент 1 («hotfix + Stage 0 + Stage 1 core») — атомарная замена слоя обратной связи дашборда. Что меняется по симптомам:

- **«An unhandled error has occurred» / мёртвые кнопки** → page-level `ErrorBoundary` в `MainLayout` + per-panel `PanelErrorBoundary`: исключение рендера больше не fault-ит circuit, панель деградирует в placeholder с Retry.
- **Кнопки «не работают»** → все admin-команды идут через `UiCommandService`: спиннер + disabled на кнопке, success/error toast внизу справа (вне scroll-viewport), журнал операций; клиентские invoke получили таймаут 20 s (`DashboardTelemetryClient`), зависший серверный вызов больше не вешает UI навсегда.
- **Деструктивные действия** → двухшаговое inline-подтверждение (`ConfirmActionButton`: Unload, Kill, Disconnect).
- **Пустой VRAM-бар** → адаптивный режим hardware-панели: `Gpu` / `UnifiedMemory` (CPU-only бейдж + бар native heap вместо мёртвого VRAM) / `NoTelemetry`.

Контракты Core/Hub не тронуты — существующие тесты не ломаются. `NativeLogTerminal.razor` и `app.css` не изменяются; вся новая стилизация самодостаточна в `dashboard-ux.css` (префикс `ux-`, коллизий с legacy CSS нет).

---

File: src/InstantAIGate.Dashboard/Services/UiCommandIds.cs

```csharp
namespace InstantAIGate.Dashboard.Services;

/// <summary>
/// Stable identifiers for every user-initiated dashboard command.
/// Ids are the key of the per-command state machine in <see cref="UiCommandService"/>,
/// so buttons anywhere in the tree can query running/failed state without prop drilling.
/// </summary>
public static class UiCommandIds
{
    public const string Connect = "session.connect";
    public const string Disconnect = "session.disconnect";
    public const string ForceGc = "admin.force-gc";
    public const string PurgeAll = "admin.purge-all";
    public const string ForceGcAndPurgeAll = "admin.gc-purge-all";
    public const string SetQueueLimit = "admin.queue-limit";

    public static string PurgeModel(string repoId) => $"admin.purge:{repoId}";

    public static string UnloadModel(string repoId) => $"admin.unload:{repoId}";

    public static string KillSession(string sessionId) => $"admin.kill:{sessionId}";
}
```

File: src/InstantAIGate.Dashboard/Services/UiCommandState.cs

```csharp
namespace InstantAIGate.Dashboard.Services;

using System;

/// <summary>Lifecycle of a single user-initiated command.</summary>
public enum UiCommandStatus
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>Immutable per-command state exposed to buttons for spinner/disabled rendering.</summary>
public sealed record UiCommandState
{
    public static UiCommandState Idle { get; } = new() { Status = UiCommandStatus.Idle };

    public UiCommandStatus Status { get; init; }

    public string? LastMessage { get; init; }

    public DateTimeOffset? LastCompletedAt { get; init; }

    public bool IsRunning => Status == UiCommandStatus.Running;
}

/// <summary>Terminal notification published once per command execution (feeds toasts).</summary>
public sealed record UiCommandEvent(
    string CommandId,
    string Label,
    UiCommandStatus Status,
    string? Message,
    DateTimeOffset Timestamp);

/// <summary>Audit trail entry retained in the in-memory operation journal.</summary>
public sealed record OperationRecord(
    DateTimeOffset Timestamp,
    string CommandId,
    string Label,
    UiCommandStatus Status,
    string? Message);
```

File: src/InstantAIGate.Dashboard/Services/UiCommandService.cs

```csharp
namespace InstantAIGate.Dashboard.Services;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Single execution point for every user-initiated dashboard command.
/// Why: before this service existed, each button handler had its own try/catch and a page-top
/// error banner that was invisible at the click site; an uncaught exception anywhere faulted
/// the whole Blazor circuit and silently killed all controls.
/// Guarantees: (1) no exception ever escapes <see cref="RunAsync"/> — failures become
/// <see cref="UiCommandStatus.Failed"/> state + journal record + toast event;
/// (2) the same command id cannot execute concurrently (duplicate starts are rejected);
/// (3) terminal transitions are published via <see cref="CommandCompleted"/> for global feedback.
/// </summary>
public sealed class UiCommandService
{
    public const int JournalCapacity = 50;

    private readonly ConcurrentDictionary<string, bool> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UiCommandState> _states = new(StringComparer.Ordinal);
    private readonly CircularBuffer<OperationRecord> _journal = new(JournalCapacity);
    private readonly ILogger<UiCommandService> _logger;

    public UiCommandService(ILogger<UiCommandService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised on every terminal command transition (succeeded or failed).</summary>
    public event Action<UiCommandEvent>? CommandCompleted;

    public UiCommandState GetState(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return _states.TryGetValue(commandId, out UiCommandState? state) ? state : UiCommandState.Idle;
    }

    public bool IsRunning(string commandId) => GetState(commandId).IsRunning;

    /// <summary>Chronological snapshot of the last <see cref="JournalCapacity"/> operations.</summary>
    public IReadOnlyList<OperationRecord> GetJournal() => _journal.Snapshot();

    /// <summary>
    /// Executes a command with full feedback bookkeeping.
    /// Returns true on success; false when the action threw, was cancelled, or a duplicate
    /// execution of the same command id was already in flight.
    /// </summary>
    public async Task<bool> RunAsync(
        string commandId,
        string label,
        Func<CancellationToken, Task> action,
        string? successMessage = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);

        if (!_running.TryAdd(commandId, true))
        {
            _logger.LogDebug("Command {CommandId} rejected: already running.", commandId);
            return false;
        }

        try
        {
            await action(ct);
            Record(commandId, label, UiCommandStatus.Succeeded, successMessage ?? $"{label} completed.");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Record(commandId, label, UiCommandStatus.Failed, $"{label} cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command {CommandId} ({Label}) failed.", commandId, label);
            Record(commandId, label, UiCommandStatus.Failed, ex.Message);
            return false;
        }
        finally
        {
            _running.TryRemove(commandId, out _);
        }
    }

    private void Record(string commandId, string label, UiCommandStatus status, string? message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _states[commandId] = new UiCommandState
        {
            Status = status,
            LastMessage = message,
            LastCompletedAt = now
        };
        _journal.Add(new OperationRecord(now, commandId, label, status, message));
        CommandCompleted?.Invoke(new UiCommandEvent(commandId, label, status, message, now));
    }
}
```

File: src/InstantAIGate.Dashboard/Services/DashboardTelemetryClient.cs

```csharp
namespace InstantAIGate.Dashboard.Services;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Scoped SignalR client for the diagnostic dashboard.
/// Owns the admin connection lifecycle, the 1 Hz telemetry ring buffers and all
/// control-plane invocations. Every invoke is bounded by <see cref="DefaultInvokeTimeout"/>
/// so a hanging server-side drain (e.g. UnloadModelAsync waiting on live leases) can never
/// freeze the UI indefinitely.
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

    public CircularBuffer<LogEntry> Logs { get; } = new(DefaultLogCapacity);

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
            await InvokeWithTimeoutAsync(
                token => _connection.InvokeAsync("SubscribeToDashboardTelemetry", token),
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

        return await InvokeWithTimeoutAsync(
            token => _connection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync", token),
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

    private async Task InvokeAdminAsync(string method, CancellationToken ct, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "Dashboard telemetry client is not connected. Reconnect before issuing admin commands.");
        }

        object[] payload = args.Where(a => a is not null).Cast<object>().ToArray();
        await InvokeWithTimeoutAsync(token => _connection.InvokeAsync(method, payload, token), ct);
    }

    /// <summary>
    /// Bounds every RPC with <see cref="DefaultInvokeTimeout"/> linked to the caller token.
    /// Why: server-side admin operations may block on native drains; without a client bound
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
```

File: src/InstantAIGate.Dashboard/Components/ConfirmActionButton.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@implements IDisposable

<button type="button"
        class="@ButtonCss"
        disabled="@(_isBusy || _isDisabled)"
        @onclick="HandleClickAsync">
    @if (_isBusy)
    {
        <span class="ux-spinner" aria-hidden="true"></span>
    }
    @CurrentLabel
</button>

@code {
    /// <summary>Default label shown in the idle state.</summary>
    [Parameter] public string Label { get; set; } = "Run";

    /// <summary>
    /// When set, the button becomes two-step: first click arms it with this label for
    /// <see cref="ConfirmWindowSeconds"/>, second click executes. Used for destructive
    /// operations (unload, kill session, disconnect) so a misclick cannot free VRAM.
    /// </summary>
    [Parameter] public string? ConfirmLabel { get; set; }

    [Parameter] public bool IsBusy { get; set; }

    [Parameter] public bool IsDisabled { get; set; }

    [Parameter] public string CssClass { get; set; } = "ux-btn";

    [Parameter] public EventCallback OnConfirm { get; set; }

    [Parameter] public int ConfirmWindowSeconds { get; set; } = 4;

    private bool _armed;
    private bool _disposed;
    private CancellationTokenSource? _disarmCts;

    private string CurrentLabel => _armed ? ConfirmLabel ?? Label : Label;

    private string ButtonCss => _armed ? $"{CssClass} ux-btn-arm" : CssClass;

    private async Task HandleClickAsync()
    {
        if (ConfirmLabel is null)
        {
            await OnConfirm.InvokeAsync();
            return;
        }

        if (!_armed)
        {
            Arm();
            return;
        }

        Disarm();
        await OnConfirm.InvokeAsync();
    }

    private void Arm()
    {
        _armed = true;
        _disarmCts = new CancellationTokenSource();
        _ = DisarmAfterAsync(_disarmCts.Token);
    }

    private async Task DisarmAfterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(ConfirmWindowSeconds), ct);
            if (!ct.IsCancellationRequested && !_disposed)
            {
                _armed = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Disarm()
    {
        _armed = false;
        if (_disarmCts is not null)
        {
            _disarmCts.Cancel();
            _disarmCts.Dispose();
            _disarmCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disarm();
    }
}
```

File: src/InstantAIGate.Dashboard/Components/PanelErrorBoundary.razor

```razor
@namespace InstantAIGate.Dashboard.Components

<ErrorBoundary @ref="_boundary">
    <ChildContent>@ChildContent</ChildContent>
    <ErrorContent>
        <div class="ux-panel-fault">
            <span class="ux-panel-fault-title">This panel failed to render</span>
            <span class="ux-panel-fault-detail">@context.Message</span>
            <span class="ux-panel-fault-hint">Telemetry is still streaming; other panels are unaffected.</span>
            <button type="button" class="ux-btn ux-btn-ghost" @onclick="Retry">Retry panel</button>
        </div>
    </ErrorContent>
</ErrorBoundary>

@code {
    /// <summary>
    /// Per-panel error containment. Why: a single bad render frame inside one diagnostic
    /// panel must not fault the whole Blazor circuit (which previously killed every control
    /// on the page until a manual reload).
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private ErrorBoundary? _boundary;

    private void Retry() => _boundary?.Recover();
}
```

File: src/InstantAIGate.Dashboard/Components/ToastHost.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@implements IDisposable
@using System.Linq
@inject UiCommandService Commands

<div class="ux-toast-stack" role="status" aria-live="polite">
    @foreach (var toast in _toasts)
    {
        <div class="ux-toast @(toast.Status == UiCommandStatus.Succeeded ? "ux-toast-ok" : "ux-toast-err")">
            <div class="ux-toast-body">
                <span class="ux-toast-title">@toast.Label</span>
                <span class="ux-toast-message">@toast.Message</span>
            </div>
            <button type="button"
                    class="ux-toast-dismiss"
                    aria-label="Dismiss notification"
                    @onclick="() => Dismiss(toast.Id)">×</button>
        </div>
    }
</div>

@code {
    private sealed record Toast(int Id, string Label, string Message, UiCommandStatus Status);

    private const int MaxVisibleToasts = 5;
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(9);

    private readonly List<Toast> _toasts = new();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private int _nextId = 1;

    /// <summary>
    /// Global feedback overlay pinned to the viewport bottom-right.
    /// Why: the previous error banner rendered at the top of the page and was outside the
    /// scroll viewport when the user clicked a button at the bottom — feedback was invisible.
    /// </summary>
    protected override void OnInitialized()
    {
        Commands.CommandCompleted += OnCommandCompleted;
    }

    private void OnCommandCompleted(UiCommandEvent e)
    {
        _ = InvokeAsync(() =>
        {
            string message = e.Message
                ?? (e.Status == UiCommandStatus.Succeeded ? "Completed." : "Failed.");
            var toast = new Toast(_nextId++, e.Label, message, e.Status);
            _toasts.Add(toast);
            while (_toasts.Count > MaxVisibleToasts)
            {
                RemoveToast(_toasts[0]);
            }

            ArmTimer(toast);
            StateHasChanged();
        });
    }

    private void ArmTimer(Toast toast)
    {
        var cts = new CancellationTokenSource();
        _timers[toast.Id] = cts;
        TimeSpan lifetime = toast.Status == UiCommandStatus.Succeeded ? SuccessLifetime : FailureLifetime;
        _ = DismissAfterAsync(toast.Id, lifetime, cts.Token);
    }

    private async Task DismissAfterAsync(int id, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                {
                    Toast? toast = _toasts.FirstOrDefault(t => t.Id == id);
                    if (toast is not null)
                    {
                        RemoveToast(toast);
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Dismiss(int id)
    {
        Toast? toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast is not null)
        {
            RemoveToast(toast);
            StateHasChanged();
        }
    }

    private void RemoveToast(Toast toast)
    {
        _toasts.Remove(toast);
        if (_timers.TryGetValue(toast.Id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
            _timers.Remove(toast.Id);
        }
    }

    public void Dispose()
    {
        Commands.CommandCompleted -= OnCommandCompleted;
        foreach (CancellationTokenSource cts in _timers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _timers.Clear();
    }
}
```

File: src/InstantAIGate.Dashboard/Components/OperationJournalPanel.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@implements IDisposable
@using System.Globalization
@inject UiCommandService Commands

<details class="ux-journal">
    <summary class="ux-journal-summary">Operation journal (@_records.Count)</summary>
    @if (_records.Count == 0)
    {
        <p class="ux-journal-empty">No admin operations executed yet.</p>
    }
    else
    {
        <ul class="ux-journal-list">
            @for (int i = _records.Count - 1; i >= 0; i--)
            {
                var record = _records[i];
                <li class="@(record.Status == UiCommandStatus.Succeeded ? "ux-journal-ok" : "ux-journal-err")">
                    <span class="ux-journal-time">@record.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)</span>
                    <span class="ux-journal-label">@record.Label</span>
                    <span class="ux-journal-msg">@record.Message</span>
                </li>
            }
        </ul>
    }
</details>

@code {
    /// <summary>
    /// Persistent audit trail of executed admin commands (newest first).
    /// Why: toasts auto-dismiss; operators need to reconstruct "what did I press and what
    /// happened" after the fact without opening browser devtools.
    /// </summary>
    private IReadOnlyList<OperationRecord> _records = [];

    protected override void OnInitialized()
    {
        Commands.CommandCompleted += OnCommandCompleted;
        _records = Commands.GetJournal();
    }

    private void OnCommandCompleted(UiCommandEvent e)
    {
        _ = InvokeAsync(() =>
        {
            _records = Commands.GetJournal();
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        Commands.CommandCompleted -= OnCommandCompleted;
    }
}
```

File: src/InstantAIGate.Dashboard/Components/MemoryLeakDetectorPanel.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@using System.Globalization
@using InstantAIGate.Core.Dtos.Status
@using InstantAIGate.Dashboard.Services

<section class="ux-panel">
    <header class="ux-panel-head">
        <h2 class="ux-panel-title">Memory Leak Radar (Hardware vs Application)</h2>
        @switch (ViewMode)
        {
            case HardwareViewMode.Gpu:
                <span class="ux-badge ux-badge-ok">@Snapshot!.Hardware.GpuProviderName</span>
                break;
            case HardwareViewMode.UnifiedMemory:
                <span class="ux-badge ux-badge-warn">CPU-only / unified memory</span>
                break;
            default:
                <span class="ux-badge">awaiting telemetry</span>
                break;
        }
    </header>

    @if (ViewMode == HardwareViewMode.NoTelemetry)
    {
        <p class="ux-panel-empty">Waiting for the first telemetry snapshot…</p>
    }
    else
    {
        <div class="ux-cards">
            @if (ViewMode == HardwareViewMode.Gpu)
            {
                <article class="ux-card">
                    <h3 class="ux-card-title">VRAM (GPU)</h3>
                    <p class="ux-card-value">@FormatBytes(Snapshot!.Hardware.VramUsedBytes) / @FormatBytes(Snapshot.Hardware.VramTotalBytes)</p>
                    <div class="ux-bar">
                        <div class="ux-bar-fill @GetBarClass(VramPercent)" style="width: @FormatPercent(VramPercent)%"></div>
                    </div>
                    <dl class="ux-kv">
                        <div><dt>Process VRAM</dt><dd>@FormatBytes(Snapshot.Hardware.VramProcessUsedBytes)</dd></div>
                        <div><dt>Model weights</dt><dd>@FormatBytes(_report.TotalModelWeightsBytes)</dd></div>
                        <div><dt>KV cache (est.)</dt><dd>@FormatBytes(_report.EstimatedKvCacheBytes)</dd></div>
                        <div><dt>Unaccounted delta</dt><dd>@FormatBytes(_report.UnaccountedVramDeltaBytes)</dd></div>
                    </dl>
                </article>
            }
            else
            {
                <article class="ux-card">
                    <h3 class="ux-card-title">Unified memory (CPU-only host)</h3>
                    <p class="ux-card-note">
                        GPU telemetry is unavailable on this host, so model weights and KV caches live in system RAM.
                        The radar tracks the process native heap instead of VRAM.
                    </p>
                    <p class="ux-card-value">@FormatBytes(_report.ProcessPrivateBytes) / @FormatBytes(Snapshot!.Hardware.SystemTotalRamBytes)</p>
                    <div class="ux-bar">
                        <div class="ux-bar-fill @GetBarClass(ProcessPrivatePercent)" style="width: @FormatPercent(ProcessPrivatePercent)%"></div>
                    </div>
                    <dl class="ux-kv">
                        <div><dt>Native heap (Private − GC)</dt><dd>@FormatBytes(_report.PrivateUnmanagedBytes)</dd></div>
                        <div><dt>Weights + KV (est.)</dt><dd>@FormatBytes(_report.EstimatedGatewayVramBytes)</dd></div>
                        <div><dt>Managed heap (GC)</dt><dd>@FormatBytes(_report.ManagedHeapBytes)</dd></div>
                    </dl>
                </article>
            }

            <article class="ux-card">
                <h3 class="ux-card-title">RAM (host &amp; process)</h3>
                <p class="ux-card-value">@FormatBytes(HostRamUsedBytes) / @FormatBytes(Snapshot!.Hardware.SystemTotalRamBytes)</p>
                <div class="ux-bar">
                    <div class="ux-bar-fill @GetBarClass(HostRamPercent)" style="width: @FormatPercent(HostRamPercent)%"></div>
                </div>
                <dl class="ux-kv">
                    <div><dt>Process working set</dt><dd>@FormatBytes(_report.ProcessWorkingSetBytes)</dd></div>
                    <div><dt>Process private bytes</dt><dd>@FormatBytes(_report.ProcessPrivateBytes)</dd></div>
                    <div><dt>CPU usage</dt><dd>@FormatPercent(Snapshot.Hardware.CpuUsagePercent)%</dd></div>
                    <div><dt>Allocated contexts</dt><dd>@_report.TotalAllocatedContexts</dd></div>
                </dl>
            </article>
        </div>

        @if (_report.IsNativeRetentionSuspected && !string.IsNullOrWhiteSpace(_report.RetentionDiagnosticMessage))
        {
            <p class="ux-alert ux-alert-danger">@_report.RetentionDiagnosticMessage</p>
        }

        @if (ViewMode == HardwareViewMode.Gpu && _report.IsVramSpilloverRisk)
        {
            <p class="ux-alert ux-alert-warn">VRAM utilization above 95%: OS page spillover over PCIe may collapse eval TPS.</p>
        }

        @if (History is { Count: >= 2 })
        {
            <figure class="ux-graph">
                <svg viewBox="0 0 600 150" preserveAspectRatio="none" role="img" aria-label="Memory and context history">
                    <polygon class="ux-graph-area" points="@BuildMemoryAreaPoints()" />
                    <polyline class="ux-graph-line" points="@BuildMemoryLinePoints()" />
                    <polyline class="ux-graph-line-ctx" points="@BuildContextsLinePoints()" />
                </svg>
                <figcaption class="ux-legend">
                    <span class="ux-legend-item"><i class="ux-swatch ux-swatch-mem"></i>physical memory (VRAM or native heap)</span>
                    <span class="ux-legend-item"><i class="ux-swatch ux-swatch-ctx"></i>allocated contexts</span>
                </figcaption>
            </figure>
        }
    }
</section>

@code {
    private enum HardwareViewMode
    {
        NoTelemetry = 0,
        Gpu = 1,
        UnifiedMemory = 2
    }

    [Parameter] public DashboardTelemetrySnapshot? Snapshot { get; set; }

    [Parameter] public IReadOnlyList<DashboardTelemetrySnapshot>? History { get; set; }

    private MemoryLeakReport _report = new();

    protected override void OnParametersSet()
    {
        _report = MemoryLeakAnalyzer.Analyze(Snapshot, History);
    }

    /// <summary>
    /// Adaptive presentation mode. Why: on CPU-only hosts every VRAM probe legitimately
    /// reports zero, and rendering a permanent 0% bar looked like a broken control.
    /// </summary>
    private HardwareViewMode ViewMode => Snapshot is null
        ? HardwareViewMode.NoTelemetry
        : (Snapshot.Hardware.VramTotalBytes > 0 ? HardwareViewMode.Gpu : HardwareViewMode.UnifiedMemory);

    private double VramPercent
    {
        get
        {
            long total = Snapshot?.Hardware.VramTotalBytes ?? 0;
            return total > 0 ? Math.Clamp((double)(Snapshot?.Hardware.VramUsedBytes ?? 0) / total * 100.0, 0.0, 100.0) : 0.0;
        }
    }

    private double ProcessPrivatePercent
    {
        get
        {
            long total = Snapshot?.Hardware.SystemTotalRamBytes ?? 0;
            return total > 0 ? Math.Clamp((double)_report.ProcessPrivateBytes / total * 100.0, 0.0, 100.0) : 0.0;
        }
    }

    private long HostRamUsedBytes
    {
        get
        {
            long total = Snapshot?.Hardware.SystemTotalRamBytes ?? 0;
            long avail = Snapshot?.Hardware.SystemAvailableRamBytes ?? 0;
            return Math.Max(0, total - avail);
        }
    }

    private double HostRamPercent
    {
        get
        {
            long total = Snapshot?.Hardware.SystemTotalRamBytes ?? 0;
            return total > 0 ? Math.Clamp((double)HostRamUsedBytes / total * 100.0, 0.0, 100.0) : 0.0;
        }
    }

    private long PeakMemoryBytes
    {
        get
        {
            if (History is null || History.Count == 0)
            {
                return GetSampleMemoryBytes(Snapshot);
            }

            long max = 0;
            for (int i = 0; i < History.Count; i++)
            {
                long val = GetSampleMemoryBytes(History[i]);
                if (val > max) max = val;
            }

            return max;
        }
    }

    private int PeakContextsCount
    {
        get
        {
            int max = 4;
            if (History is not null)
            {
                for (int i = 0; i < History.Count; i++)
                {
                    int c = GetSampleContextsCount(History[i]);
                    if (c > max) max = c;
                }
            }

            return max;
        }
    }

    private long GetSampleMemoryBytes(DashboardTelemetrySnapshot? s)
    {
        if (s is null) return 0;
        if (s.Hardware.VramTotalBytes > 0)
        {
            return s.Hardware.VramProcessUsedBytes > 0
                ? s.Hardware.VramProcessUsedBytes
                : s.Hardware.VramUsedBytes;
        }

        long priv = s.Hardware.ProcessPrivateBytes > 0
            ? s.Hardware.ProcessPrivateBytes
            : s.Hardware.ProcessRamBytes;
        return Math.Max(0, priv - s.Hardware.ManagedHeapBytes);
    }

    private static int GetSampleContextsCount(DashboardTelemetrySnapshot? s)
    {
        if (s?.Models is null) return 0;
        int sum = 0;
        for (int i = 0; i < s.Models.Count; i++)
        {
            sum += s.Models[i].ActiveLeases + s.Models[i].IdlePooledContexts;
        }

        return sum;
    }

    private string BuildMemoryLinePoints()
    {
        if (History is null || History.Count < 2) return string.Empty;
        long maxMem = Math.Max(1, PeakMemoryBytes);
        return BuildPolyline(History.Count, i => (double)GetSampleMemoryBytes(History[i]) / maxMem);
    }

    private string BuildMemoryAreaPoints()
    {
        string line = BuildMemoryLinePoints();
        return string.IsNullOrEmpty(line) ? string.Empty : $"0,145 {line} 600,145";
    }

    private string BuildContextsLinePoints()
    {
        if (History is null || History.Count < 2) return string.Empty;
        int maxCtx = Math.Max(1, PeakContextsCount);
        return BuildPolyline(History.Count, i => (double)GetSampleContextsCount(History[i]) / maxCtx);
    }

    private static string BuildPolyline(int count, Func<int, double> normalizedValueSelector)
    {
        const double width = 600.0;
        const double topY = 15.0;
        const double bottomY = 145.0;
        double usableHeight = bottomY - topY;
        var parts = new string[count];
        for (int i = 0; i < count; i++)
        {
            double x = (double)i / (count - 1) * width;
            double ratio = Math.Clamp(normalizedValueSelector(i), 0.0, 1.0);
            double y = bottomY - (ratio * usableHeight);
            parts[i] = string.Create(CultureInfo.InvariantCulture, $"{x:F1},{y:F1}");
        }

        return string.Join(' ', parts);
    }

    private static string GetBarClass(double percent) =>
        percent >= 95.0 ? "danger" : (percent >= 80.0 ? "warn" : "ok");

    private static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 100.0).ToString("F1", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }
}
```

File: src/InstantAIGate.Dashboard/Components/ModelSlotsGrid.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@using System.Globalization
@using InstantAIGate.Core.Dtos.Config
@using InstantAIGate.Core.Dtos.Status
@using InstantAIGate.Dashboard.Services
@inject UiCommandService Commands

<section class="ux-panel">
    <header class="ux-panel-head">
        <h2 class="ux-panel-title">Model &amp; Context Pool Matrix</h2>
        <div class="ux-panel-actions">
            <ConfirmActionButton Label="Purge Idle Contexts (All)"
                                 CssClass="ux-btn ux-btn-ghost"
                                 IsBusy="@Commands.IsRunning(UiCommandIds.PurgeAll)"
                                 OnConfirm="() => OnPurgeIdleContexts.InvokeAsync(string.Empty)" />
            <ConfirmActionButton Label="Force .NET GC"
                                 CssClass="ux-btn ux-btn-ghost"
                                 IsBusy="@Commands.IsRunning(UiCommandIds.ForceGc)"
                                 OnConfirm="() => OnForceGc.InvokeAsync()" />
        </div>
    </header>

    @if (Models is null || Models.Count == 0)
    {
        <p class="ux-panel-empty">No models are loaded in the VRAM pool.</p>
    }
    else
    {
        <div class="ux-model-grid">
            @foreach (var model in Models)
            {
                <article class="ux-model-card">
                    <header class="ux-model-head">
                        <span class="ux-badge @GetModelTypeCss(model.Type)">@model.Type</span>
                        <span class="ux-model-id">@model.RepoId</span>
                        @if (model.IsDraining)
                        {
                            <span class="ux-badge ux-badge-warn">draining</span>
                        }
                    </header>

                    <dl class="ux-kv">
                        <div><dt>GPU layers</dt><dd>@model.GpuLayers / @model.TotalLayers</dd></div>
                        <div><dt>Context</dt><dd>@model.ContextSize.ToString("N0", CultureInfo.InvariantCulture)</dd></div>
                        <div><dt>KV cache</dt><dd>@model.KvCacheQuantization</dd></div>
                        <div><dt>Weights</dt><dd>@FormatBytes(model.ModelWeightsBytes)</dd></div>
                    </dl>

                    <div class="ux-slots" aria-label="Context slots">
                        @for (int slotIndex = 1; slotIndex <= Math.Max(1, model.MaxContexts); slotIndex++)
                        {
                            var slot = GetSlotState(model, slotIndex);
                            <span class="ux-slot @slot.Css" title="@slot.Title">@slotIndex</span>
                        }
                    </div>
                    <p class="ux-model-leases">@model.ActiveLeases active lease(s), @model.IdlePooledContexts idle pooled.</p>

                    <footer class="ux-model-actions">
                        <ConfirmActionButton Label="Purge Idle"
                                             CssClass="ux-btn ux-btn-ghost"
                                             IsBusy="@Commands.IsRunning(UiCommandIds.PurgeModel(model.RepoId))"
                                             OnConfirm="() => OnPurgeIdleContexts.InvokeAsync(model.RepoId)" />
                        <ConfirmActionButton Label="Unload"
                                             ConfirmLabel="Confirm unload?"
                                             CssClass="ux-btn ux-btn-danger"
                                             IsBusy="@Commands.IsRunning(UiCommandIds.UnloadModel(model.RepoId))"
                                             OnConfirm="() => OnUnloadModel.InvokeAsync(model.RepoId)" />
                    </footer>
                </article>
            }
        </div>
    }
</section>

@code {
    private sealed record SlotState(string Css, string Title);

    [Parameter] public IReadOnlyList<ModelPoolTelemetryDto>? Models { get; set; }

    [Parameter] public EventCallback<string> OnPurgeIdleContexts { get; set; }

    [Parameter] public EventCallback<string> OnUnloadModel { get; set; }

    [Parameter] public EventCallback OnForceGc { get; set; }

    private static string GetModelTypeCss(ModelType type) => type switch
    {
        ModelType.Vlm => "ux-type-vlm",
        ModelType.Embedding => "ux-type-embed",
        ModelType.Audio => "ux-type-audio",
        _ => "ux-type-llm"
    };

    private static SlotState GetSlotState(ModelPoolTelemetryDto model, int slotIndex)
    {
        if (slotIndex <= model.ActiveLeases)
        {
            return new SlotState("ux-slot-active", "Slot leased by an active session");
        }

        if (slotIndex <= model.ActiveLeases + model.IdlePooledContexts)
        {
            return new SlotState("ux-slot-idle", "Idle native context pooled in VRAM");
        }

        return new SlotState("ux-slot-free", "Unallocated slot");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }
}
```

File: src/InstantAIGate.Dashboard/Components/ActiveSessionsTable.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@using System.Globalization
@using InstantAIGate.Core.Dtos.Status
@using InstantAIGate.Dashboard.Services
@inject UiCommandService Commands

<section class="ux-panel">
    <header class="ux-panel-head">
        <h2 class="ux-panel-title">Active &amp; Hanging Sessions Inspector</h2>
    </header>

    @if (Sessions is null || Sessions.Count == 0)
    {
        <p class="ux-panel-empty">No active sessions. Slots are free.</p>
    }
    else
    {
        <div class="ux-table-wrap">
            <table class="ux-table">
                <thead>
                    <tr>
                        <th>Session</th>
                        <th>Model</th>
                        <th>State</th>
                        <th>Context fill</th>
                        <th>Prompt TPS</th>
                        <th>Eval TPS</th>
                        <th>Idle</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var session in Sessions)
                    {
                        double fillPercent = session.MaxContextTokens > 0
                            ? Math.Clamp((double)session.PastTokens / session.MaxContextTokens * 100.0, 0.0, 100.0)
                            : 0.0;
                        <tr>
                            <td class="ux-mono" title="@session.SessionId">@Truncate(session.SessionId, 14)</td>
                            <td class="ux-mono">@session.RepoId</td>
                            <td><span class="ux-badge @GetStateCss(session.State)">@GetStateLabel(session.State)</span></td>
                            <td>
                                <div class="ux-bar ux-bar-thin">
                                    <div class="ux-bar-fill @GetContextBarClass(fillPercent)" style="width: @FormatPercent(fillPercent)%"></div>
                                </div>
                                <span class="ux-table-sub">@session.PastTokens.ToString("N0", CultureInfo.InvariantCulture) / @session.MaxContextTokens.ToString("N0", CultureInfo.InvariantCulture) tok</span>
                            </td>
                            <td>@session.PromptTps.ToString("F1", CultureInfo.InvariantCulture)</td>
                            <td>@session.EvalTps.ToString("F1", CultureInfo.InvariantCulture)</td>
                            <td>@FormatDuration(session.IdleSeconds)</td>
                            <td>
                                <ConfirmActionButton Label="Kill & Free VRAM"
                                                     ConfirmLabel="Confirm kill?"
                                                     CssClass="ux-btn ux-btn-danger ux-btn-sm"
                                                     IsBusy="@Commands.IsRunning(UiCommandIds.KillSession(session.SessionId))"
                                                     OnConfirm="() => OnKillSession.InvokeAsync(session.SessionId)" />
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    }
</section>

@code {
    [Parameter] public IReadOnlyList<SessionTelemetryDto>? Sessions { get; set; }

    [Parameter] public EventCallback<string> OnKillSession { get; set; }

    private static string GetContextBarClass(double fillPercent) =>
        fillPercent >= 85.0 ? "danger" : (fillPercent >= 60.0 ? "warn" : "ok");

    private static string GetStateCss(SessionStateType state) => state switch
    {
        SessionStateType.Generating => "ux-state-generating",
        SessionStateType.DisconnectedGracePeriod => "ux-state-grace",
        _ => "ux-state-idle"
    };

    private static string GetStateLabel(SessionStateType state) => state switch
    {
        SessionStateType.Generating => "GENERATING",
        SessionStateType.DisconnectedGracePeriod => "DISCONNECTED (GRACE)",
        _ => "IDLE"
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");

    private static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 100.0).ToString("F1", CultureInfo.InvariantCulture);

    private static string FormatDuration(double seconds)
    {
        if (seconds < 1.0) return "0s";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1.0)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }

        if (ts.TotalMinutes >= 1.0)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        return $"{ts.Seconds}s";
    }
}
```

File: src/InstantAIGate.Dashboard/Components/PerformanceTpsPanel.razor

```razor
@namespace InstantAIGate.Dashboard.Components
@using System.Globalization
@using InstantAIGate.Core.Dtos.Status
@using InstantAIGate.Dashboard.Services
@inject UiCommandService Commands

<section class="ux-panel">
    <header class="ux-panel-head">
        <h2 class="ux-panel-title">Real-Time Performance &amp; Queue Monitor</h2>
    </header>

    <div class="ux-cards">
        <article class="ux-card">
            <h3 class="ux-card-title">Generation speed (eval TPS)</h3>
            <p class="ux-card-value">@CurrentEvalTps.ToString("F1", CultureInfo.InvariantCulture) <span class="ux-card-unit">tok/s</span></p>
            <dl class="ux-kv">
                <div><dt>Peak (2 min)</dt><dd>@PeakEvalTps.ToString("F1", CultureInfo.InvariantCulture)</dd></div>
            </dl>
        </article>

        <article class="ux-card">
            <h3 class="ux-card-title">Prompt ingestion (prompt TPS)</h3>
            <p class="ux-card-value">@CurrentPromptTps.ToString("F1", CultureInfo.InvariantCulture) <span class="ux-card-unit">tok/s</span></p>
            <dl class="ux-kv">
                <div><dt>Peak (2 min)</dt><dd>@PeakPromptTps.ToString("F1", CultureInfo.InvariantCulture)</dd></div>
            </dl>
        </article>

        <article class="ux-card">
            <h3 class="ux-card-title">Inference queue</h3>
            <p class="ux-card-value">@PendingRequests / @MaxQueueSize</p>
            <div class="ux-bar">
                <div class="ux-bar-fill @GetBarClass(QueueUtilizationPercent)" style="width: @FormatPercent(QueueUtilizationPercent)%"></div>
            </div>
            <div class="ux-inline-form">
                <label class="ux-field-label" for="ux-queue-limit">Queue limit</label>
                <input id="ux-queue-limit"
                       class="ux-input"
                       type="number"
                       min="1"
                       @bind="_queueLimitInput" />
                <ConfirmActionButton Label="Apply"
                                       CssClass="ux-btn ux-btn-ghost ux-btn-sm"
                                       IsBusy="@Commands.IsRunning(UiCommandIds.SetQueueLimit)"
                                       OnConfirm="ApplyQueueLimitAsync" />
            </div>
        </article>
    </div>

    @if (History is { Count: >= 2 })
    {
        <figure class="ux-graph">
            <svg viewBox="0 0 600 150" preserveAspectRatio="none" role="img" aria-label="TPS history">
                <polygon class="ux-graph-area" points="@BuildEvalTpsAreaPoints()" />
                <polyline class="ux-graph-line" points="@BuildEvalTpsLinePoints()" />
                <polyline class="ux-graph-line-ctx" points="@BuildPromptTpsLinePoints()" />
            </svg>
            <figcaption class="ux-legend">
                <span class="ux-legend-item"><i class="ux-swatch ux-swatch-mem"></i>aggregate eval TPS</span>
                <span class="ux-legend-item"><i class="ux-swatch ux-swatch-ctx"></i>max prompt TPS</span>
            </figcaption>
        </figure>
    }
</section>

@code {
    [Parameter] public DashboardTelemetrySnapshot? Snapshot { get; set; }

    [Parameter] public IReadOnlyList<DashboardTelemetrySnapshot>? History { get; set; }

    [Parameter] public EventCallback<int> OnUpdateQueueLimit { get; set; }

    private int _queueLimitInput = 100;
    private int _lastSyncedQueueLimit = 100;

    protected override void OnParametersSet()
    {
        int incomingLimit = Snapshot?.InferenceMetrics.MaxQueueSize ?? 100;
        if (incomingLimit > 0 && incomingLimit != _lastSyncedQueueLimit)
        {
            _lastSyncedQueueLimit = incomingLimit;
            _queueLimitInput = incomingLimit;
        }
    }

    private int PendingRequests => Snapshot?.InferenceMetrics.PendingRequests ?? 0;

    private int MaxQueueSize => Snapshot?.InferenceMetrics.MaxQueueSize is > 0
        ? Snapshot.InferenceMetrics.MaxQueueSize
        : 100;

    private double QueueUtilizationPercent =>
        MaxQueueSize > 0 ? Math.Clamp((double)PendingRequests / MaxQueueSize * 100.0, 0.0, 100.0) : 0.0;

    private double CurrentEvalTps => ComputeAggregateEvalTps(Snapshot);

    private double CurrentPromptTps => ComputeAggregatePromptTps(Snapshot);

    private double PeakEvalTps
    {
        get
        {
            double max = CurrentEvalTps;
            if (History is not null)
            {
                for (int i = 0; i < History.Count; i++)
                {
                    double val = ComputeAggregateEvalTps(History[i]);
                    if (val > max) max = val;
                }
            }

            return max;
        }
    }

    private double PeakPromptTps
    {
        get
        {
            double max = CurrentPromptTps;
            if (History is not null)
            {
                for (int i = 0; i < History.Count; i++)
                {
                    double val = ComputeAggregatePromptTps(History[i]);
                    if (val > max) max = val;
                }
            }

            return max;
        }
    }

    private static double ComputeAggregateEvalTps(DashboardTelemetrySnapshot? snapshot)
    {
        if (snapshot?.Sessions is null || snapshot.Sessions.Count == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < snapshot.Sessions.Count; i++)
        {
            sum += Math.Max(0.0, snapshot.Sessions[i].EvalTps);
        }

        return sum;
    }

    private static double ComputeAggregatePromptTps(DashboardTelemetrySnapshot? snapshot)
    {
        if (snapshot?.Sessions is null || snapshot.Sessions.Count == 0) return 0.0;
        double max = 0.0;
        for (int i = 0; i < snapshot.Sessions.Count; i++)
        {
            if (snapshot.Sessions[i].PromptTps > max)
            {
                max = snapshot.Sessions[i].PromptTps;
            }
        }

        return max;
    }

    private async Task ApplyQueueLimitAsync()
    {
        if (_queueLimitInput > 0 && OnUpdateQueueLimit.HasDelegate)
        {
            await OnUpdateQueueLimit.InvokeAsync(_queueLimitInput);
        }
    }

    private string BuildEvalTpsLinePoints()
    {
        if (History is null || History.Count < 2) return string.Empty;
        double scaleMax = Math.Max(10.0, PeakEvalTps);
        return BuildPolyline(History.Count, i => ComputeAggregateEvalTps(History[i]) / scaleMax);
    }

    private string BuildEvalTpsAreaPoints()
    {
        string line = BuildEvalTpsLinePoints();
        return string.IsNullOrEmpty(line) ? string.Empty : $"0,145 {line} 600,145";
    }

    private string BuildPromptTpsLinePoints()
    {
        if (History is null || History.Count < 2) return string.Empty;
        double scaleMax = Math.Max(10.0, PeakPromptTps);
        return BuildPolyline(History.Count, i => ComputeAggregatePromptTps(History[i]) / scaleMax);
    }

    private static string BuildPolyline(int count, Func<int, double> normalizedSelector)
    {
        const double width = 600.0;
        const double topY = 15.0;
        const double bottomY = 145.0;
        double height = bottomY - topY;
        var points = new string[count];
        for (int i = 0; i < count; i++)
        {
            double x = (double)i / (count - 1) * width;
            double ratio = Math.Clamp(normalizedSelector(i), 0.0, 1.0);
            double y = bottomY - (ratio * height);
            points[i] = string.Create(CultureInfo.InvariantCulture, $"{x:F1},{y:F1}");
        }

        return string.Join(' ', points);
    }

    private static string GetBarClass(double percent) =>
        percent >= 95.0 ? "danger" : (percent >= 80.0 ? "warn" : "ok");

    private static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 100.0).ToString("F1", CultureInfo.InvariantCulture);
}
```

File: src/InstantAIGate.Dashboard/Pages/Index.razor

```razor
@page "/"
@implements IAsyncDisposable
@using System.Globalization
@using InstantAIGate.Core.Dtos.Status
@using InstantAIGate.SSR.Dtos
@using InstantAIGate.Dashboard.Services
@using InstantAIGate.Dashboard.Components

<header class="ux-topbar">
    <div class="ux-brand">
        <span class="ux-brand-name">InstantAIGate</span>
        <span class="ux-brand-sub">Telemetry &amp; Leak Diagnostics</span>
    </div>
    <div class="ux-pills">
        <span class="ux-pill @ConnectionCssClass">SignalR: @StateText</span>
        <span class="ux-pill @GetGatewayStatusCss(EffectiveGatewayStatus.Status)">
            Gateway: @EffectiveGatewayStatus.Status
            @if (!string.IsNullOrWhiteSpace(EffectiveGatewayStatus.ActiveModelId))
            {
                <text>(@EffectiveGatewayStatus.ActiveModelId)</text>
            }
        </span>
        @if (_latestSnapshot is not null)
        {
            <span class="ux-pill ux-pill-dim">Updated: @_latestSnapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)</span>
        }
    </div>
    <div class="ux-topbar-actions">
        <ConfirmActionButton Label="Force GC & Purge Idle Pools"
                             CssClass="ux-btn ux-btn-primary"
                             IsBusy="@Commands.IsRunning(UiCommandIds.ForceGcAndPurgeAll)"
                             IsDisabled="@(_connectionState != ConnectionState.Connected)"
                             OnConfirm="ForceGcAndPurgeAllAsync" />
        <ConfirmActionButton Label="Disconnect"
                             ConfirmLabel="Confirm disconnect?"
                             CssClass="ux-btn ux-btn-ghost"
                             IsBusy="@Commands.IsRunning(UiCommandIds.Disconnect)"
                             IsDisabled="@(_connectionState != ConnectionState.Connected)"
                             OnConfirm="DisconnectAsync" />
    </div>
</header>

@if (_connectionState == ConnectionState.Reconnecting)
{
    <div class="ux-strip ux-strip-warn">SignalR transport lost. Reconnecting — telemetry resumes automatically.</div>
}

@if (_requiresApiKey)
{
    <section class="ux-gate">
        <h1 class="ux-gate-title">Admin access required</h1>
        <p class="ux-gate-note">The dashboard streams privileged telemetry over SignalR. Enter the gateway Admin API key to connect.</p>
        <div class="ux-inline-form">
            <input class="ux-input ux-input-wide"
                   type="password"
                   placeholder="Admin API key"
                   autocomplete="off"
                   @bind="_apiKeyInput"
                   @bind:event="oninput" />
            <button type="button"
                    class="ux-btn ux-btn-primary"
                    disabled="@_isConnecting"
                    @onclick="ConnectAsync">
                @if (_isConnecting)
                {
                    <span class="ux-spinner" aria-hidden="true"></span>
                }
                @(_isConnecting ? "Connecting..." : "Connect Dashboard")
            </button>
        </div>
        @if (!string.IsNullOrWhiteSpace(_connectError))
        {
            <p class="ux-alert ux-alert-danger">@_connectError</p>
        }
    </section>
}
else
{
    <OperationJournalPanel />

    @if (EffectiveGatewayStatus.Status == GatewayOperationalStatus.ModelDownloading || _latestDownloadProgress is not null)
    {
        <section class="ux-panel">
            <header class="ux-panel-head">
                <h2 class="ux-panel-title">Startup model download</h2>
            </header>
            <p class="ux-card-value">
                @(_latestDownloadProgress?.ModelId ?? EffectiveGatewayStatus.ActiveModelId ?? string.Empty)
                — @FormatPercent(_latestDownloadProgress?.Percentage ?? EffectiveGatewayStatus.ProgressPercentage)%
            </p>
            <div class="ux-bar">
                <div class="ux-bar-fill ok" style="width: @FormatPercent(_latestDownloadProgress?.Percentage ?? EffectiveGatewayStatus.ProgressPercentage)%"></div>
            </div>
        </section>
    }

    <PanelErrorBoundary>
        <MemoryLeakDetectorPanel Snapshot="_latestSnapshot" History="_historySnapshot" />
    </PanelErrorBoundary>

    <PanelErrorBoundary>
        <PerformanceTpsPanel Snapshot="_latestSnapshot" History="_historySnapshot" OnUpdateQueueLimit="UpdateQueueLimitAsync" />
    </PanelErrorBoundary>

    <PanelErrorBoundary>
        <ModelSlotsGrid Models="_latestSnapshot?.Models"
                        OnPurgeIdleContexts="PurgeIdleContextsAsync"
                        OnUnloadModel="UnloadModelAsync"
                        OnForceGc="ForceGcAsync" />
    </PanelErrorBoundary>

    <PanelErrorBoundary>
        <ActiveSessionsTable Sessions="_latestSnapshot?.Sessions" OnKillSession="ForceKillSessionAsync" />
    </PanelErrorBoundary>

    <PanelErrorBoundary>
        <NativeLogTerminal Logs="_logSnapshot" OnClear="ClearLogs" />
    </PanelErrorBoundary>
}

@code {
    [Inject] private DashboardTelemetryClient _client { get; set; } = default!;

    [Inject] private IAdminApiKeyStore _apiKeyStore { get; set; } = default!;

    [Inject] private UiCommandService Commands { get; set; } = default!;

    private DashboardTelemetrySnapshot? _latestSnapshot;
    private IReadOnlyList<DashboardTelemetrySnapshot> _historySnapshot = [];
    private IReadOnlyList<LogEntry> _logSnapshot = [];
    private GatewayStatusDetails? _latestStatus;
    private DownloadProgress? _latestDownloadProgress;
    private bool _requiresApiKey = true;
    private bool _isConnecting;
    private string? _apiKeyInput;
    private string? _connectError;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    private GatewayStatusDetails EffectiveGatewayStatus =>
        _latestStatus ?? _latestSnapshot?.GatewayStatus ?? new GatewayStatusDetails();

    protected override async Task OnInitializedAsync()
    {
        _client.DashboardTelemetryReceived += OnDashboardTelemetryReceived;
        _client.GatewayStatusChanged += OnGatewayStatus;
        _client.DownloadProgressChanged += OnDownloadProgress;
        _client.LogReceived += OnLogReceived;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
        Commands.CommandCompleted += OnCommandCompleted;

        string? storedKey = await _apiKeyStore.GetApiKeyAsync();
        if (!string.IsNullOrWhiteSpace(storedKey))
        {
            _apiKeyInput = storedKey;
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKeyInput))
        {
            _connectError = "API key cannot be empty.";
            return;
        }

        _isConnecting = true;
        _connectError = null;
        StateHasChanged();
        try
        {
            await _apiKeyStore.SetApiKeyAsync(_apiKeyInput.Trim());
            await _client.ConnectAsync();
            _requiresApiKey = false;
            _latestSnapshot = await _client.GetSnapshotAsync();
            _historySnapshot = _client.History.Snapshot();
            _logSnapshot = _client.Logs.Snapshot();
        }
        catch (Exception ex)
        {
            _connectError = ex.Message;
            await _apiKeyStore.ClearAsync();
        }
        finally
        {
            _isConnecting = false;
            StateHasChanged();
        }
    }

    private Task ForceGcAndPurgeAllAsync() => Commands.RunAsync(
        UiCommandIds.ForceGcAndPurgeAll,
        "Force GC & purge idle pools",
        async ct =>
        {
            await _client.PurgeIdleContextsAsync(string.Empty, ct);
            await _client.TriggerGarbageCollectionAsync(ct);
            await RefreshSnapshotSafeAsync(ct);
        },
        "GC forced and idle pools purged.");

    private Task ForceGcAsync() => Commands.RunAsync(
        UiCommandIds.ForceGc,
        "Force .NET GC",
        async ct =>
        {
            await _client.TriggerGarbageCollectionAsync(ct);
            await RefreshSnapshotSafeAsync(ct);
        },
        "Garbage collection completed.");

    private Task PurgeIdleContextsAsync(string repoId)
    {
        bool all = string.IsNullOrWhiteSpace(repoId);
        string commandId = all ? UiCommandIds.PurgeAll : UiCommandIds.PurgeModel(repoId);
        string label = all ? "Purge idle contexts (all models)" : $"Purge idle contexts ({repoId})";
        return Commands.RunAsync(
            commandId,
            label,
            async ct =>
            {
                await _client.PurgeIdleContextsAsync(repoId, ct);
                await RefreshSnapshotSafeAsync(ct);
            },
            "Idle contexts purged.");
    }

    private Task UnloadModelAsync(string repoId) => Commands.RunAsync(
        UiCommandIds.UnloadModel(repoId),
        $"Unload model {repoId}",
        async ct =>
        {
            await _client.UnloadModelAsync(repoId, ct);
            await RefreshSnapshotSafeAsync(ct);
        },
        $"Model {repoId} unloaded from the pool.");

    private Task ForceKillSessionAsync(string sessionId) => Commands.RunAsync(
        UiCommandIds.KillSession(sessionId),
        $"Kill session {sessionId}",
        async ct =>
        {
            await _client.ForceKillSessionAsync(sessionId, ct);
            await RefreshSnapshotSafeAsync(ct);
        },
        $"Session {sessionId} destroyed and its VRAM slot freed.");

    private Task UpdateQueueLimitAsync(int newLimit) => Commands.RunAsync(
        UiCommandIds.SetQueueLimit,
        $"Set queue limit to {newLimit}",
        async ct =>
        {
            await _client.SetQueueLimitAsync(newLimit, ct);
            await RefreshSnapshotSafeAsync(ct);
        },
        $"Inference queue limit set to {newLimit}.");

    private async Task DisconnectAsync()
    {
        await Commands.RunAsync(
            UiCommandIds.Disconnect,
            "Disconnect dashboard",
            async ct =>
            {
                await _client.DisconnectAsync(ct);
                await _apiKeyStore.ClearAsync(ct);
                _requiresApiKey = true;
                _apiKeyInput = string.Empty;
            },
            "Dashboard disconnected.");
        StateHasChanged();
    }

    private async Task RefreshSnapshotSafeAsync(CancellationToken ct)
    {
        try
        {
            _latestSnapshot = await _client.GetSnapshotAsync(ct) ?? _latestSnapshot;
            _historySnapshot = _client.History.Snapshot();
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private void ClearLogs()
    {
        _client.Logs.Clear();
        _logSnapshot = [];
        StateHasChanged();
    }

    private void OnDashboardTelemetryReceived(DashboardTelemetrySnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        _historySnapshot = _client.History.Snapshot();
        if (snapshot.GatewayStatus.Status != GatewayOperationalStatus.ModelDownloading)
        {
            _latestDownloadProgress = null;
        }

        InvokeAsync(StateHasChanged);
    }

    private void OnGatewayStatus(GatewayStatusDetails status)
    {
        _latestStatus = status;
        if (status.Status != GatewayOperationalStatus.ModelDownloading)
        {
            _latestDownloadProgress = null;
        }

        InvokeAsync(StateHasChanged);
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        _latestDownloadProgress = progress;
        InvokeAsync(StateHasChanged);
    }

    private void OnLogReceived(string level, string category, string message)
    {
        _logSnapshot = _client.Logs.Snapshot();
        InvokeAsync(StateHasChanged);
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        _connectionState = state;
        if (state == ConnectionState.Connected)
        {
            _ = InvokeAsync(() => RefreshSnapshotSafeAsync(CancellationToken.None));
        }

        InvokeAsync(StateHasChanged);
    }

    private void OnCommandCompleted(UiCommandEvent e)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private string StateText => _connectionState switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting…",
        ConnectionState.Reconnecting => "Reconnecting…",
        _ => "Disconnected"
    };

    private string ConnectionCssClass => _connectionState switch
    {
        ConnectionState.Connected => "ux-pill-ok",
        ConnectionState.Reconnecting => "ux-pill-warn",
        _ => "ux-pill-err"
    };

    private static string GetGatewayStatusCss(GatewayOperationalStatus status) => status switch
    {
        GatewayOperationalStatus.Ready => "ux-pill-ok",
        GatewayOperationalStatus.ModelDownloading or GatewayOperationalStatus.ModelLoading => "ux-pill-warn",
        GatewayOperationalStatus.Faulted => "ux-pill-err",
        _ => "ux-pill-dim"
    };

    private static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 100.0).ToString("F1", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        _client.DashboardTelemetryReceived -= OnDashboardTelemetryReceived;
        _client.GatewayStatusChanged -= OnGatewayStatus;
        _client.DownloadProgressChanged -= OnDownloadProgress;
        _client.LogReceived -= OnLogReceived;
        _client.ConnectionStateChanged -= OnConnectionStateChanged;
        Commands.CommandCompleted -= OnCommandCompleted;
        try { await _client.DisposeAsync(); } catch { }
    }
}
```

File: src/InstantAIGate.Dashboard/Layout/MainLayout.razor

```razor
@inherits LayoutComponentBase
@using InstantAIGate.Dashboard.Components

<div class="ig-page">
    <main class="ig-main">
        <article class="ig-content">
            <ErrorBoundary @ref="_pageBoundary">
                <ChildContent>@Body</ChildContent>
                <ErrorContent>
                    <div class="ux-page-fault">
                        <h2>Dashboard hit an unexpected UI error</h2>
                        <p class="ux-page-fault-detail">@context.Message</p>
                        <p class="ux-page-fault-hint">
                            The telemetry connection is still alive; only the render pipeline was interrupted.
                            Retry rendering, or reload the page if the fault repeats.
                        </p>
                        <button type="button" class="ux-btn ux-btn-primary" @onclick="RecoverPage">Retry rendering</button>
                    </div>
                </ErrorContent>
            </ErrorBoundary>
        </article>
    </main>
    <ToastHost />
</div>

@code {
    /// <summary>
    /// Page-level error containment. Why: without a boundary any render exception faults the
    /// entire Blazor WebAssembly circuit, after which no @onclick handler executes and the
    /// only visible artifact is the static "An unhandled error has occurred" strip.
    /// </summary>
    private ErrorBoundary? _pageBoundary;

    private void RecoverPage() => _pageBoundary?.Recover();
}
```

File: src/InstantAIGate.Dashboard/Program.cs

```csharp
namespace InstantAIGate.Dashboard;

using InstantAIGate.Dashboard.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        });

        var hubUrl = new Uri(
            new Uri(builder.HostEnvironment.BaseAddress),
            DashboardTelemetryClient.HubRelativePath);

        builder.Services.AddScoped<IAdminApiKeyStore, LocalStorageAdminApiKeyStore>();
        builder.Services.AddScoped<UiCommandService>();
        builder.Services.AddScoped(sp => new DashboardTelemetryClient(
            sp.GetRequiredService<IAdminApiKeyStore>(),
            sp.GetRequiredService<ILogger<DashboardTelemetryClient>>(),
            hubUrl));

        await builder.Build().RunAsync();
    }
}
```

File: src/InstantAIGate.Dashboard/wwwroot/css/dashboard-ux.css

```css
/* InstantAIGate dashboard UX layer. Self-contained: every class is prefixed with ux-
   so it never collides with the legacy app.css vocabulary. */

:root {
    --ux-bg: #0d1117;
    --ux-surface: #161b22;
    --ux-surface-2: #1c2330;
    --ux-border: #2a3140;
    --ux-text: #d7dde6;
    --ux-text-dim: #8b949e;
    --ux-accent: #58a6ff;
    --ux-ok: #3fb950;
    --ux-warn: #d29922;
    --ux-danger: #f85149;
}

.ux-topbar {
    position: sticky;
    top: 0;
    z-index: 900;
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    align-items: center;
    justify-content: space-between;
    padding: 10px 16px;
    background: var(--ux-surface);
    border-bottom: 1px solid var(--ux-border);
}

.ux-brand { display: flex; flex-direction: column; line-height: 1.2; }
.ux-brand-name { font-weight: 700; font-size: clamp(15px, 1.4vw, 18px); color: var(--ux-accent); }
.ux-brand-sub { font-size: 12px; color: var(--ux-text-dim); }

.ux-pills { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
.ux-pill {
    padding: 3px 10px;
    border: 1px solid var(--ux-border);
    border-radius: 999px;
    font-size: 12px;
    color: var(--ux-text);
    background: var(--ux-surface-2);
}
.ux-pill-ok { border-color: var(--ux-ok); color: var(--ux-ok); }
.ux-pill-warn { border-color: var(--ux-warn); color: var(--ux-warn); }
.ux-pill-err { border-color: var(--ux-danger); color: var(--ux-danger); }
.ux-pill-dim { color: var(--ux-text-dim); }

.ux-topbar-actions { display: flex; gap: 8px; flex-wrap: wrap; }

.ux-strip { padding: 8px 16px; font-size: 13px; border-bottom: 1px solid var(--ux-border); }
.ux-strip-warn { background: rgba(210, 153, 34, 0.12); color: var(--ux-warn); }

/* Buttons */
.ux-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    font-size: 13px;
    font-family: inherit;
    color: var(--ux-text);
    background: var(--ux-surface-2);
    border: 1px solid var(--ux-border);
    border-radius: 6px;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease;
}
.ux-btn:hover:not(:disabled) { background: #232b3a; }
.ux-btn:disabled { opacity: 0.55; cursor: not-allowed; }
.ux-btn-primary { background: rgba(88, 166, 255, 0.16); border-color: var(--ux-accent); color: var(--ux-accent); }
.ux-btn-ghost { background: transparent; }
.ux-btn-danger { background: rgba(248, 81, 73, 0.14); border-color: var(--ux-danger); color: var(--ux-danger); }
.ux-btn-arm { background: rgba(248, 81, 73, 0.28); border-color: var(--ux-danger); color: #fff; }
.ux-btn-sm { padding: 3px 8px; font-size: 12px; }

.ux-spinner {
    width: 12px;
    height: 12px;
    border: 2px solid rgba(255, 255, 255, 0.35);
    border-top-color: #fff;
    border-radius: 50%;
    animation: ux-spin 0.8s linear infinite;
    display: inline-block;
}
@keyframes ux-spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) {
    .ux-spinner { animation: none; }
}

/* Toasts: pinned to the viewport, immune to page scroll position */
.ux-toast-stack {
    position: fixed;
    right: 16px;
    bottom: 16px;
    z-index: 1000;
    display: flex;
    flex-direction: column;
    gap: 8px;
    max-width: 400px;
}
.ux-toast {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    padding: 10px 12px;
    background: var(--ux-surface);
    border: 1px solid var(--ux-border);
    border-left-width: 4px;
    border-radius: 6px;
    box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
}
.ux-toast-ok { border-left-color: var(--ux-ok); }
.ux-toast-err { border-left-color: var(--ux-danger); }
.ux-toast-body { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.ux-toast-title { font-size: 13px; font-weight: 600; color: var(--ux-text); }
.ux-toast-message { font-size: 12px; color: var(--ux-text-dim); word-break: break-word; }
.ux-toast-dismiss {
    border: none;
    background: transparent;
    color: var(--ux-text-dim);
    font-size: 16px;
    line-height: 1;
    cursor: pointer;
    padding: 2px 4px;
}
.ux-toast-dismiss:hover { color: var(--ux-text); }

/* Journal */
.ux-journal { margin: 12px 16px 0; font-size: 12px; }
.ux-journal-summary { cursor: pointer; color: var(--ux-text-dim); padding: 4px 0; }
.ux-journal-empty { color: var(--ux-text-dim); margin: 6px 0; }
.ux-journal-list { list-style: none; margin: 6px 0 0; padding: 0; max-height: 180px; overflow-y: auto; }
.ux-journal-list li { display: flex; gap: 8px; padding: 3px 0; border-bottom: 1px dashed var(--ux-border); }
.ux-journal-time { color: var(--ux-text-dim); font-family: monospace; }
.ux-journal-label { color: var(--ux-text); }
.ux-journal-msg { color: var(--ux-text-dim); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ux-journal-ok .ux-journal-label { color: var(--ux-ok); }
.ux-journal-err .ux-journal-label { color: var(--ux-danger); }

/* Panels & cards */
.ux-panel {
    margin: 12px 16px;
    padding: 14px 16px;
    background: var(--ux-surface);
    border: 1px solid var(--ux-border);
    border-radius: 8px;
}
.ux-panel-head { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.ux-panel-title { margin: 0; font-size: clamp(14px, 1.3vw, 17px); font-weight: 600; }
.ux-panel-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.ux-panel-empty { color: var(--ux-text-dim); font-size: 13px; }

.ux-cards { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); }
.ux-card { padding: 10px 12px; background: var(--ux-surface-2); border: 1px solid var(--ux-border); border-radius: 6px; }
.ux-card-title { margin: 0 0 6px; font-size: 13px; font-weight: 600; color: var(--ux-text-dim); text-transform: uppercase; letter-spacing: 0.04em; }
.ux-card-value { margin: 0 0 6px; font-size: 15px; font-weight: 600; }
.ux-card-unit { font-size: 12px; color: var(--ux-text-dim); font-weight: 400; }
.ux-card-note { margin: 0 0 8px; font-size: 12px; color: var(--ux-text-dim); }

.ux-kv { margin: 8px 0 0; display: grid; gap: 3px; font-size: 12px; }
.ux-kv div { display: flex; justify-content: space-between; gap: 12px; }
.ux-kv dt { color: var(--ux-text-dim); }
.ux-kv dd { margin: 0; color: var(--ux-text); font-family: monospace; }

/* Bars */
.ux-bar { height: 8px; background: #0a0e14; border-radius: 4px; overflow: hidden; }
.ux-bar-thin { height: 6px; }
.ux-bar-fill { height: 100%; border-radius: 4px; transition: width 240ms ease; }
.ux-bar-fill.ok { background: var(--ux-ok); }
.ux-bar-fill.warn { background: var(--ux-warn); }
.ux-bar-fill.danger { background: var(--ux-danger); }

/* Badges */
.ux-badge {
    padding: 2px 8px;
    border-radius: 4px;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.03em;
    background: var(--ux-surface-2);
    border: 1px solid var(--ux-border);
    color: var(--ux-text-dim);
}
.ux-badge-ok { border-color: var(--ux-ok); color: var(--ux-ok); }
.ux-badge-warn { border-color: var(--ux-warn); color: var(--ux-warn); }
.ux-type-llm { border-color: var(--ux-accent); color: var(--ux-accent); }
.ux-type-vlm { border-color: #bc8cff; color: #bc8cff; }
.ux-type-embed { border-color: #56d364; color: #56d364; }
.ux-type-audio { border-color: var(--ux-warn); color: var(--ux-warn); }
.ux-state-generating { border-color: var(--ux-ok); color: var(--ux-ok); }
.ux-state-idle { border-color: var(--ux-accent); color: var(--ux-accent); }
.ux-state-grace { border-color: var(--ux-warn); color: var(--ux-warn); }

/* Alerts */
.ux-alert { margin: 10px 0 0; padding: 8px 10px; border-radius: 6px; font-size: 13px; }
.ux-alert-danger { background: rgba(248, 81, 73, 0.12); color: var(--ux-danger); border: 1px solid var(--ux-danger); }
.ux-alert-warn { background: rgba(210, 153, 34, 0.12); color: var(--ux-warn); border: 1px solid var(--ux-warn); }

/* Graphs */
.ux-graph { margin: 12px 0 0; }
.ux-graph svg { width: 100%; height: 150px; background: #0a0e14; border: 1px solid var(--ux-border); border-radius: 6px; }
.ux-graph-area { fill: rgba(88, 166, 255, 0.14); stroke: none; }
.ux-graph-line { fill: none; stroke: var(--ux-accent); stroke-width: 2; }
.ux-graph-line-ctx { fill: none; stroke: var(--ux-warn); stroke-width: 1.5; stroke-dasharray: 4 3; }
.ux-legend { display: flex; gap: 16px; margin-top: 6px; font-size: 12px; color: var(--ux-text-dim); }
.ux-legend-item { display: inline-flex; align-items: center; gap: 6px; }
.ux-swatch { width: 14px; height: 3px; display: inline-block; }
.ux-swatch-mem { background: var(--ux-accent); }
.ux-swatch-ctx { background: var(--ux-warn); }

/* Model grid */
.ux-model-grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); }
.ux-model-card { padding: 12px; background: var(--ux-surface-2); border: 1px solid var(--ux-border); border-radius: 6px; display: flex; flex-direction: column; gap: 8px; }
.ux-model-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.ux-model-id { font-family: monospace; font-size: 13px; font-weight: 600; }
.ux-model-leases { margin: 0; font-size: 12px; color: var(--ux-text-dim); }
.ux-model-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: auto; }

.ux-slots { display: flex; gap: 6px; flex-wrap: wrap; }
.ux-slot {
    width: 26px;
    height: 26px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border-radius: 4px;
    font-size: 12px;
    font-family: monospace;
    border: 1px solid var(--ux-border);
    color: var(--ux-text-dim);
    background: #0a0e14;
}
.ux-slot-active { border-color: var(--ux-ok); color: var(--ux-ok); background: rgba(63, 185, 80, 0.12); }
.ux-slot-idle { border-color: var(--ux-accent); color: var(--ux-accent); background: rgba(88, 166, 255, 0.10); }
.ux-slot-free { opacity: 0.6; }

/* Table */
.ux-table-wrap { overflow-x: auto; }
.ux-table { width: 100%; border-collapse: collapse; font-size: 12px; }
.ux-table th, .ux-table td { padding: 6px 8px; text-align: left; border-bottom: 1px solid var(--ux-border); vertical-align: middle; }
.ux-table th { color: var(--ux-text-dim); font-weight: 600; text-transform: uppercase; font-size: 11px; letter-spacing: 0.04em; }
.ux-mono { font-family: monospace; }
.ux-table-sub { display: block; color: var(--ux-text-dim); font-size: 11px; margin-top: 2px; }

/* Forms */
.ux-inline-form { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-top: 8px; }
.ux-field-label { font-size: 12px; color: var(--ux-text-dim); }
.ux-input {
    padding: 6px 8px;
    font-size: 13px;
    font-family: inherit;
    color: var(--ux-text);
    background: #0a0e14;
    border: 1px solid var(--ux-border);
    border-radius: 6px;
    width: 110px;
}
.ux-input-wide { width: min(360px, 100%); }
.ux-input:focus { outline: 1px solid var(--ux-accent); }

/* Gate */
.ux-gate { margin: 24px 16px; padding: 20px; background: var(--ux-surface); border: 1px solid var(--ux-border); border-radius: 8px; max-width: 560px; }
.ux-gate-title { margin: 0 0 8px; font-size: 18px; }
.ux-gate-note { margin: 0 0 12px; font-size: 13px; color: var(--ux-text-dim); }

/* Error containment surfaces */
.ux-page-fault { margin: 24px 16px; padding: 20px; border: 1px solid var(--ux-danger); border-radius: 8px; background: rgba(248, 81, 73, 0.08); }
.ux-page-fault h2 { margin: 0 0 8px; font-size: 17px; color: var(--ux-danger); }
.ux-page-fault-detail { margin: 0 0 8px; font-family: monospace; font-size: 12px; color: var(--ux-text); word-break: break-word; }
.ux-page-fault-hint { margin: 0 0 12px; font-size: 13px; color: var(--ux-text-dim); }

.ux-panel-fault {
    display: flex;
    flex-direction: column;
    gap: 6px;
    align-items: flex-start;
    margin: 12px 16px;
    padding: 14px 16px;
    border: 1px dashed var(--ux-warn);
    border-radius: 8px;
    background: rgba(210, 153, 34, 0.08);
}
.ux-panel-fault-title { font-weight: 600; color: var(--ux-warn); font-size: 14px; }
.ux-panel-fault-detail { font-family: monospace; font-size: 12px; color: var(--ux-text); word-break: break-word; }
.ux-panel-fault-hint { font-size: 12px; color: var(--ux-text-dim); }
```

File: src/InstantAIGate.Dashboard/wwwroot/index.html

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
    <title>InstantAIGate — Diagnostic Dashboard</title>
    <base href="/" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="stylesheet" href="css/dashboard-ux.css" />
</head>
<body>
    <div id="app">
        <div class="ig-loading">
            <strong>InstantAIGate</strong>
            <span>Loading diagnostic dashboard…</span>
        </div>
    </div>

    <div id="blazor-error-ui" data-noscript="">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">🗙</a>
    </div>
    <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

File: tests/InstantAIGate.Core.Tests/Dashboard/UiCommandServiceTests.cs

```csharp
namespace InstantAIGate.Core.Tests.Dashboard;

using InstantAIGate.Dashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class UiCommandServiceTests
{
    private static UiCommandService CreateService() => new(NullLogger<UiCommandService>.Instance);

    [Fact]
    public async Task RunAsync_WhenActionSucceeds_PublishesSucceededEventAndState()
    {
        var service = CreateService();
        UiCommandEvent? received = null;
        service.CommandCompleted += e => received = e;

        bool result = await service.RunAsync("cmd-1", "Test command", _ => Task.CompletedTask, "done.");

        Assert.True(result);
        Assert.NotNull(received);
        Assert.Equal(UiCommandStatus.Succeeded, received.Status);
        Assert.Equal("done.", received.Message);
        Assert.Equal(UiCommandStatus.Succeeded, service.GetState("cmd-1").Status);
        Assert.False(service.IsRunning("cmd-1"));
        Assert.Single(service.GetJournal());
    }

    [Fact]
    public async Task RunAsync_WhenActionThrows_SwallowsExceptionAndPublishesFailedEvent()
    {
        var service = CreateService();
        UiCommandEvent? received = null;
        service.CommandCompleted += e => received = e;

        bool result = await service.RunAsync("cmd-2", "Boom", _ => throw new InvalidOperationException("server said no"));

        Assert.False(result);
        Assert.NotNull(received);
        Assert.Equal(UiCommandStatus.Failed, received.Status);
        Assert.Equal("server said no", received.Message);
        Assert.Equal(UiCommandStatus.Failed, service.GetState("cmd-2").Status);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledExternally_ReportsFailedWithoutThrowing()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        bool result = await service.RunAsync(
            "cmd-3",
            "Cancellable",
            async ct =>
            {
                cts.Cancel();
                await Task.Delay(50, ct);
            },
            ct: cts.Token);

        Assert.False(result);
        Assert.Equal(UiCommandStatus.Failed, service.GetState("cmd-3").Status);
    }

    [Fact]
    public async Task RunAsync_ConcurrentDuplicateCommandId_IsRejected()
    {
        var service = CreateService();
        var gate = new SemaphoreSlim(0, 1);

        Task first = service.RunAsync("cmd-4", "Slow", async _ =>
        {
            gate.Release();
            await Task.Delay(200);
        });
        await gate.WaitAsync(TimeSpan.FromSeconds(2));

        bool second = await service.RunAsync("cmd-4", "Slow", _ => Task.CompletedTask);

        Assert.False(second);
        await first;
        Assert.Equal(UiCommandStatus.Succeeded, service.GetState("cmd-4").Status);
    }

    [Fact]
    public async Task Journal_IsCappedAtCapacity()
    {
        var service = CreateService();
        for (int i = 0; i < UiCommandService.JournalCapacity + 10; i++)
        {
            await service.RunAsync($"cmd-{i}", $"Op {i}", _ => Task.CompletedTask);
        }

        Assert.Equal(UiCommandService.JournalCapacity, service.GetJournal().Count);
        Assert.EndsWith("Op 69", service.GetJournal().Last().Label, StringComparison.Ordinal);
    }
}
```
