namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using Microsoft.Extensions.Logging;
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

        public StatefulSessionEntry(SessionStartRequest request, DateTimeOffset createdAt)
        {
            Request = request;
            LastAccessed = createdAt;
        }

        public void Touch(DateTimeOffset now) => LastAccessed = now;

        public void Dispose()
        {
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
    private readonly ConcurrentDictionary<string, StatefulSessionEntry> _sessions = new();
    private readonly ITimer _cleanupTimer;
    private bool _disposed;

    public SessionInferenceManager(
        IModelManager modelManager,
        IBackendFacade backendFacade,
        ILogger<SessionInferenceManager> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? idleTimeout = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _backendFacade = backendFacade ?? throw new ArgumentNullException(nameof(backendFacade));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);

        _cleanupTimer = _timeProvider.CreateTimer(
            _ => _ = CleanupIdleSessionsAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public Task CreateSessionAsync(SessionStartRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = new StatefulSessionEntry(request, _timeProvider.GetUtcNow());

        if (!_sessions.TryAdd(request.SessionId, entry))
        {
            throw new InvalidOperationException($"Session '{request.SessionId}' is already registered.");
        }

        return Task.CompletedTask;
    }

    public Task ReleaseSessionAsync(string sessionId, bool destroySlot = false, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            if (destroySlot && session.ActiveContext != null)
            {
                session.ActiveContext.SuppressPool = true;
            }

            session.Dispose();
            _logger.LogInformation("Released session state for {SessionId} (DestroySlot: {DestroySlot})", sessionId, destroySlot);
        }
        return Task.CompletedTask;
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