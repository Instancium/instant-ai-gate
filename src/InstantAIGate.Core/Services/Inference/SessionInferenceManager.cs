namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class SessionInferenceManager : ISessionInferenceManager
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<SessionInferenceManager> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<string, StatefulSessionEntry> _sessions = new();
    private readonly ITimer _cleanupTimer;
    private bool _disposed;

    public SessionInferenceManager(
        IModelManager modelManager,
        ILogger<SessionInferenceManager> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? idleTimeout = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
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

        _logger.LogInformation("Native state session registered: {SessionId} (Model: {RepoId})", request.SessionId, request.RepoId);
        return Task.CompletedTask;
    }

    public async Task<InferenceContext> GetOrCreateContextAsync(string sessionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        }

        entry.Touch(_timeProvider.GetUtcNow());

        if (entry.Context != null)
        {
            return entry.Context;
        }

        await entry.SyncGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (entry.Context != null)
            {
                return entry.Context;
            }

            _logger.LogDebug("Acquiring fresh InferenceContext for stateful session {SessionId}", sessionId);
            var ctx = await _modelManager.AcquireContextAsync(entry.Request.RepoId, ct).ConfigureAwait(false);
            entry.SetContext(ctx);
            return ctx;
        }
        finally
        {
            entry.SyncGate.Release();
        }
    }

    public async Task<IDisposable> AcquireSessionExecutionGateAsync(string sessionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        }

        await entry.ExecutionGate.WaitAsync(ct).ConfigureAwait(false);
        entry.Touch(_timeProvider.GetUtcNow());
        return new SessionExecutionReleaser(entry.ExecutionGate);
    }

    public bool TryGetSessionRepoId(string sessionId, out string? repoId)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            repoId = entry.Request.RepoId;
            return true;
        }

        repoId = null;
        return false;
    }

    public async Task ReleaseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            await entry.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation("Native state session released: {SessionId}", sessionId);
        }
    }

    public async Task CleanupIdleSessionsAsync()
    {
        if (_disposed) return;

        var now = _timeProvider.GetUtcNow();
        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastActivityUtc > _idleTimeout)
            {
                if (_sessions.TryRemove(kvp.Key, out var expiredEntry))
                {
                    _logger.LogInformation("Evicting expired idle session {SessionId}", kvp.Key);
                    await expiredEntry.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        foreach (var kvp in _sessions)
        {
            if (_sessions.TryRemove(kvp.Key, out var entry))
            {
                entry.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    public void EnqueueTurnTokens(string sessionId, int tokenCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            entry.TurnTokenLengths.Enqueue(tokenCount);
        }
    }

    public bool TryDequeueOldestTurnTokens(string sessionId, out int tokenCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        tokenCount = 0;

        if (_sessions.TryGetValue(sessionId, out var entry) && entry.TurnTokenLengths.Count > 0)
        {
            tokenCount = entry.TurnTokenLengths.Dequeue();
            return true;
        }

        return false;
    }


    private sealed class StatefulSessionEntry : IAsyncDisposable
    {
        public SessionStartRequest Request { get; }
        public SemaphoreSlim SyncGate { get; } = new(1, 1);
        public SemaphoreSlim ExecutionGate { get; } = new(1, 1);
        public InferenceContext? Context { get; private set; }
        public DateTimeOffset LastActivityUtc { get; private set; }
        public int PastTokensCount { get; set; }
        public Queue<int> TurnTokenLengths { get; } = new Queue<int>();

        public StatefulSessionEntry(SessionStartRequest request, DateTimeOffset createdUtc)
        {
            Request = request;
            LastActivityUtc = createdUtc;
        }

        public void SetContext(InferenceContext context)
        {
            Context = context;
        }

        public void Touch(DateTimeOffset now)
        {
            LastActivityUtc = now;
        }

        public async ValueTask DisposeAsync()
        {
            await SyncGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Context?.Dispose();
                Context = null;
            }
            finally
            {
                SyncGate.Release();
                SyncGate.Dispose();
                ExecutionGate.Dispose();
            }
        }
    }

    public int GetPastTokensCount(string sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            return entry.PastTokensCount;
        }

        throw new KeyNotFoundException($"Session '{sessionId}' not found.");
    }

    public void UpdatePastTokensCount(string sessionId, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            entry.PastTokensCount = count;
        }
    }

    private sealed class SessionExecutionReleaser(SemaphoreSlim gate) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                gate.Release();
            }
        }
    }
}