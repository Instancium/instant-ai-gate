namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class SessionInferenceManager : ISessionInferenceManager, IDisposable
{
    private sealed class StatefulSessionEntry : IDisposable
    {
        public SessionStartRequest Request { get; }
        public string RepoId => Request.RepoId;
        public int PastTokens { get; set; } = 0;
        public List<int> TokenSequence { get; } = new();
        public readonly SemaphoreSlim ExecutionGate = new(1, 1);
        public readonly SemaphoreSlim ContextInitGate = new(1, 1);
        public readonly object LockObj = new();
        public InferenceContext? ActiveContext { get; set; }
        public DateTimeOffset LastAccessed { get; private set; }
        public string? BoundConnectionId { get; set; }
        public CancellationTokenSource? DisconnectGraceCts { get; set; }

        public StatefulSessionEntry(SessionStartRequest request, DateTimeOffset createdAt, string? connectionId = null)
        {
            Request = request;
            LastAccessed = createdAt;
            BoundConnectionId = connectionId;
        }

        public void Touch(DateTimeOffset now) => LastAccessed = now;

        public void CancelDisconnectGrace()
        {
            if (DisconnectGraceCts != null)
            {
                try
                {
                    DisconnectGraceCts.Cancel();
                    DisconnectGraceCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Safe to ignore if already disposed.
                }
                DisconnectGraceCts = null;
            }
        }

        public void Dispose()
        {
            CancelDisconnectGrace();
            ActiveContext?.Dispose();
            ExecutionGate.Dispose();
            ContextInitGate.Dispose();
        }
    }

    private readonly IModelManager _modelManager;
    private readonly IBackendFacade _backendFacade;
    private readonly ILogger<SessionInferenceManager> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _disconnectGracePeriod;
    private readonly ConcurrentDictionary<string, StatefulSessionEntry> _sessions = new();
    private readonly ITimer _cleanupTimer;
    private bool _disposed;

    public SessionInferenceManager(
        IModelManager modelManager,
        IBackendFacade backendFacade,
        ILogger<SessionInferenceManager> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? idleTimeout = null,
        IOptions<SessionSettings>? sessionOptions = null,
        TimeSpan? disconnectGracePeriod = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _backendFacade = backendFacade ?? throw new ArgumentNullException(nameof(backendFacade));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;

        var options = sessionOptions?.Value ?? new SessionSettings();
        _idleTimeout = idleTimeout ?? options.IdleTimeout;
        _disconnectGracePeriod = disconnectGracePeriod ?? options.DisconnectGracePeriod;

        _cleanupTimer = _timeProvider.CreateTimer(
            _ => _ = CleanupIdleSessionsAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public Task CreateSessionAsync(SessionStartRequest request, CancellationToken ct = default)
    {
        return CreateSessionAsync(request, connectionId: null, ct);
    }

    /// <summary>
    /// Creates or resumes a session with optional connection binding. Idempotent:
    /// if the session already exists (e.g. client reconnected after a network blink),
    /// it cancels any pending disconnect grace timer, rebinds the connectionId,
    /// touches the entry, and preserves the KV-cache.
    /// </summary>
    public Task CreateSessionAsync(SessionStartRequest request, string? connectionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessions.TryGetValue(request.SessionId, out var existingSession))
        {
            lock (existingSession.LockObj)
            {
                existingSession.CancelDisconnectGrace();
                existingSession.BoundConnectionId = connectionId;
                existingSession.Touch(_timeProvider.GetUtcNow());
            }

            _logger.LogInformation(
                "Resumed existing session {SessionId} after reconnect (BoundConnectionId: {ConnectionId}). KV-cache preserved.",
                request.SessionId, connectionId ?? "none");
            return Task.CompletedTask;
        }

        var entry = new StatefulSessionEntry(request, _timeProvider.GetUtcNow(), connectionId);
        if (!_sessions.TryAdd(request.SessionId, entry))
        {
            if (_sessions.TryGetValue(request.SessionId, out var racedSession))
            {
                lock (racedSession.LockObj)
                {
                    racedSession.CancelDisconnectGrace();
                    racedSession.BoundConnectionId = connectionId;
                    racedSession.Touch(_timeProvider.GetUtcNow());
                }

                _logger.LogInformation(
                    "Resumed existing session {SessionId} after reconnect (race resolved, BoundConnectionId: {ConnectionId}). KV-cache preserved.",
                    request.SessionId, connectionId ?? "none");
            }
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Created new session {SessionId} for model {RepoId} (BoundConnectionId: {ConnectionId})",
            request.SessionId, request.RepoId, connectionId ?? "none");
        return Task.CompletedTask;
    }

    public void BindConnection(string sessionId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                session.CancelDisconnectGrace();
                session.BoundConnectionId = connectionId;
                session.Touch(_timeProvider.GetUtcNow());
            }
        }
    }

    public void UnbindConnection(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                session.BoundConnectionId = null;
            }
        }
    }

    /// <summary>
    /// Called when a transport connection drops. Identifies all sessions bound
    /// to this connectionId and arms a Grace Period timer. If the client does not
    /// reconnect within the grace period, the session slot is automatically evicted.
    /// </summary>
    public void NotifyConnectionDisconnected(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;

        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;
            var sessionId = kvp.Key;

            bool isBound;
            lock (session.LockObj)
            {
                isBound = string.Equals(session.BoundConnectionId, connectionId, StringComparison.OrdinalIgnoreCase);
                if (isBound)
                {
                    session.CancelDisconnectGrace();
                    var cts = new CancellationTokenSource();
                    session.DisconnectGraceCts = cts;
                    session.BoundConnectionId = null;

                    _logger.LogInformation(
                        "Connection {ConnectionId} dropped for session {SessionId}. Grace period of {GracePeriod}s started.",
                        connectionId, sessionId, _disconnectGracePeriod.TotalSeconds);

                    _ = ScheduleGracePeriodEvictionAsync(sessionId, cts);
                }
            }
        }
    }

    private async Task ScheduleGracePeriodEvictionAsync(string sessionId, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_disconnectGracePeriod, _timeProvider, cts.Token);

            _logger.LogInformation(
                "Grace period expired for disconnected session {SessionId}. Evicting context from VRAM.",
                sessionId);

            await ReleaseSessionAsync(sessionId, destroySlot: false, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Grace period cancelled for session {SessionId}. Client reconnected successfully.",
                sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error during grace period eviction for session {SessionId}",
                sessionId);
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Releases a session. Waits for the ExecutionGate to ensure no llama_decode
    /// is in progress before disposing the native context. This prevents 0xc0000005 crashes.
    /// </summary>
    public async Task ReleaseSessionAsync(string sessionId, bool destroySlot = false, CancellationToken ct = default)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return;
        }

        try
        {
            // CRITICAL: Wait for any in-flight llama_decode to finish before
            // disposing the context. This prevents access-violation crashes.
            await session.ExecutionGate.WaitAsync(CancellationToken.None);

            if (destroySlot && session.ActiveContext != null)
            {
                session.ActiveContext.SuppressPool = true;
                _logger.LogInformation(
                    "Force-destroying session {SessionId} with VRAM slot release",
                    sessionId);
            }
            else
            {
                _logger.LogInformation(
                    "Releasing session {SessionId}, returning context to pool",
                    sessionId);
            }

            session.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Gate was already disposed (edge case during shutdown). Safe to ignore.
            _logger.LogDebug(
                "ExecutionGate already disposed during release of session {SessionId}",
                sessionId);
        }
        finally
        {
            try
            {
                session.ExecutionGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Expected if Dispose already ran.
            }
            catch (SemaphoreFullException)
            {
                // Gate was never acquired. Safe to ignore.
            }
        }
    }

    public bool TryGetSessionRepoId(string sessionId, out string? repoId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            repoId = session.RepoId;
            return true;
        }
        repoId = null;
        return false;
    }

    public async Task<IDisposable> AcquireSessionExecutionGateAsync(string sessionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' is not active.");
        }

        session.Touch(_timeProvider.GetUtcNow());
        await session.ExecutionGate.WaitAsync(ct);
        return new Releaser(session.ExecutionGate);
    }

    public async Task<InferenceContext> GetOrCreateContextAsync(string sessionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' is not active.");
        }

        session.Touch(_timeProvider.GetUtcNow());

        if (session.ActiveContext != null)
        {
            return session.ActiveContext;
        }

        await session.ContextInitGate.WaitAsync(ct);
        try
        {
            if (session.ActiveContext == null)
            {
                session.ActiveContext = await _modelManager.AcquireContextAsync(session.RepoId, ct);
            }
            return session.ActiveContext;
        }
        finally
        {
            session.ContextInitGate.Release();
        }
    }

    public async Task RollbackToPositionAsync(string sessionId, int targetTokenPosition, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        using (await AcquireSessionExecutionGateAsync(sessionId, ct))
        {
            lock (session.LockObj)
            {
                if (targetTokenPosition < 0 || targetTokenPosition > session.PastTokens)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(targetTokenPosition),
                        $"Target rollback position {targetTokenPosition} is invalid for current PastTokens={session.PastTokens}.");
                }

                if (session.ActiveContext?.TextContext?.Handle is IContextHandle handle)
                {
                    _backendFacade.RemoveContextMemoryRange(handle, 0, targetTokenPosition, -1);
                }

                session.PastTokens = targetTokenPosition;

                if (session.TokenSequence.Count > targetTokenPosition)
                {
                    session.TokenSequence.RemoveRange(
                        targetTokenPosition,
                        session.TokenSequence.Count - targetTokenPosition);
                }

                session.Touch(_timeProvider.GetUtcNow());
                _logger.LogDebug("Session {SessionId} rolled back to token position {TargetPosition}", sessionId, targetTokenPosition);
            }
        }
    }

    public async Task ShiftMemoryRangeAsync(string sessionId, int startPos, int count, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        using (await AcquireSessionExecutionGateAsync(sessionId, ct))
        {
            lock (session.LockObj)
            {
                if (startPos < 0 || count <= 0 || (startPos + count) > session.PastTokens)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(count),
                        $"Invalid range startPos={startPos}, count={count} for PastTokens={session.PastTokens}.");
                }

                if (session.ActiveContext?.TextContext?.Handle is IContextHandle handle)
                {
                    if (!_backendFacade.CanShiftContextMemory(handle))
                    {
                        throw new NotSupportedException(
                            $"Physical KV-cache shifting is not supported for session '{sessionId}' " +
                            "due to active FlashAttention or GPU offload configuration. " +
                            "Use RollbackToPositionAsync and re-decode the suffix instead.");
                    }

                    bool removed = _backendFacade.RemoveContextMemoryRange(handle, 0, startPos, startPos + count);
                    if (!removed)
                    {
                        throw new InvalidOperationException($"Failed to remove KV memory range [{startPos}, {startPos + count}) natively.");
                    }

                    _backendFacade.ShiftContextMemoryRange(handle, 0, startPos + count, -1, -count);
                }

                session.PastTokens -= count;

                if (session.TokenSequence.Count >= (startPos + count))
                {
                    session.TokenSequence.RemoveRange(startPos, count);
                }

                session.Touch(_timeProvider.GetUtcNow());

                _logger.LogDebug(
                    "Session {SessionId} shifted KV range: removed {Count} tokens starting at {StartPos}",
                    sessionId, count, startPos);
            }
        }
    }

    public void UpdatePastTokensCount(string sessionId, int count)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                session.PastTokens = count;
                session.Touch(_timeProvider.GetUtcNow());
            }
        }
    }

    public void AppendSessionTokens(string sessionId, int[] tokens)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                session.TokenSequence.AddRange(tokens);
                session.Touch(_timeProvider.GetUtcNow());
            }
        }
    }

    public int[] GetSessionPrefixTokens(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                return session.TokenSequence.ToArray();
            }
        }
        return Array.Empty<int>();
    }

    public int GetPastTokensCount(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj)
            {
                return session.PastTokens;
            }
        }
        return 0;
    }

    public async Task CleanupIdleSessionsAsync()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastAccessed > _idleTimeout)
            {
                _logger.LogInformation("Evicting expired idle session {SessionId}", kvp.Key);
                // Return to pool for idle evictions
                await ReleaseSessionAsync(kvp.Key, destroySlot: false);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _disposed;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (!_disposed)
            {
                _gate.Release();
                _disposed = true;
            }
        }
    }
}