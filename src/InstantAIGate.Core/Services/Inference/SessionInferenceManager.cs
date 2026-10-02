// File: src/InstantAIGate.Core/Services/Inference/SessionInferenceManager.cs
namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class SessionInferenceManager : ISessionInferenceManager
{
    private class StatefulSessionEntry : IDisposable
    {
        public SessionStartRequest Request { get; }
        public string RepoId => Request.RepoId;
        public int PastTokens { get; set; } = 0;
        public List<MessageTokenSpan> MessageSpans { get; } = new();
        public List<int> TokenSequence { get; } = new();

        // 1. DUAL-GATE CONCURRENCY (Fixes Deadlock)
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

        public int CalculateAdaptiveReserve(int staticMaxTokens)
        {
            var assistantSpans = MessageSpans.Where(m => m.Role == "assistant").ToList();
            if (assistantSpans.Count == 0) return staticMaxTokens;

            double avgTokens = assistantSpans.Average(m => m.EndPos - m.StartPos);
            int adaptiveReserve = (int)(avgTokens * 1.5);
            return Math.Min(adaptiveReserve, staticMaxTokens);
        }

        public void Dispose()
        {
            ActiveContext?.Dispose();
            ExecutionGate.Dispose();
            ContextInitGate.Dispose();
        }
    }

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
            _ => _ = CleanupIdleSessionsAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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

    public Task ReleaseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
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

        // Lock safely via secondary gate to avoid execution reentrancy
        if (session.ActiveContext == null)
        {
            await session.ContextInitGate.WaitAsync(ct);
            try
            {
                if (session.ActiveContext == null)
                {
                    session.ActiveContext = await _modelManager.AcquireContextAsync(session.RepoId, ct);
                }
            }
            finally
            {
                session.ContextInitGate.Release();
            }
        }

        return session.ActiveContext;
    }

    public void RecordMessageSpan(string sessionId, string role, int startPos, int endPos)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj) session.MessageSpans.Add(new MessageTokenSpan(role, startPos, endPos));
        }
    }

    public int GetAdaptiveTokenReserve(string sessionId, int staticMaxTokens)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj) return session.CalculateAdaptiveReserve(staticMaxTokens);
        }
        return staticMaxTokens;
    }

    public bool TryCalculateSemanticEviction(string sessionId, int requiredSpace, out int evictionStart, out int evictionEnd)
    {
        evictionStart = 0;
        evictionEnd = 0;

        if (!_sessions.TryGetValue(sessionId, out var session)) return false;

        lock (session.LockObj)
        {
        
            if (session.MessageSpans.Count < 2) return false;

            var systemSpan = session.MessageSpans[0];
            var firstUserSpan = session.MessageSpans.FirstOrDefault(m => m.Role == "user" && m.StartPos >= systemSpan.EndPos)
                                ?? systemSpan; 

            int protectedBoundary = firstUserSpan.StartPos + Math.Min(4, firstUserSpan.EndPos - firstUserSpan.StartPos);

            evictionStart = protectedBoundary;
            evictionEnd = protectedBoundary;
            int accumulatedSpace = 0;
            var spansToRemove = new List<MessageTokenSpan>();

            
            var evictableSpans = session.MessageSpans
                .Where(m => m.EndPos > protectedBoundary)
                .OrderBy(m => m.StartPos)
                .ToList();

            foreach (var span in evictableSpans)
            {
               
                int spanEvictionStart = Math.Max(span.StartPos, protectedBoundary);
                int spanEvictionEnd = span.EndPos;

                int spaceGained = spanEvictionEnd - spanEvictionStart;
                if (spaceGained > 0)
                {
                    accumulatedSpace += spaceGained;
                    evictionEnd = spanEvictionEnd;

                   
                    if (spanEvictionStart <= span.StartPos)
                    {
                        spansToRemove.Add(span);
                    }
                }

               
                if (accumulatedSpace >= requiredSpace)
                {
                    break;
                }
            }

           
            if (accumulatedSpace < requiredSpace) return false;

            int shiftAmount = evictionEnd - evictionStart;

           
            session.MessageSpans.RemoveAll(m => spansToRemove.Contains(m));

            
            int capturedEvictionEnd = evictionEnd;
            foreach (var remainingSpan in session.MessageSpans)
            {
               
                if (remainingSpan.StartPos < evictionStart && remainingSpan.EndPos > evictionStart)
                {
                    remainingSpan.EndPos = evictionStart;
                }

                else if (remainingSpan.StartPos >= capturedEvictionEnd)
                {
                    remainingSpan.StartPos -= shiftAmount;
                    remainingSpan.EndPos -= shiftAmount;
                }
            }

            if (session.TokenSequence.Count >= evictionStart + shiftAmount)
            {
                session.TokenSequence.RemoveRange(evictionStart, shiftAmount);
            }

            return true;
        }
    }

    public void UpdatePastTokensCount(string sessionId, int count)
    {
        if (_sessions.TryGetValue(sessionId, out var session)) session.PastTokens = count;
    }

    public int GetPastTokensCount(string sessionId) => _sessions.TryGetValue(sessionId, out var session) ? session.PastTokens : 0;

    public void AppendSessionTokens(string sessionId, int[] tokens)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj) session.TokenSequence.AddRange(tokens);
        }
    }

    public int[] GetSessionPrefixTokens(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.LockObj) return session.TokenSequence.ToArray();
        }
        return Array.Empty<int>();
    }

    public Task CleanupIdleSessionsAsync()
    {
        if (_disposed) return Task.CompletedTask;
        var now = _timeProvider.GetUtcNow();
        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastAccessed > _idleTimeout)
            {
                if (_sessions.TryRemove(kvp.Key, out var entry)) entry.Dispose();
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cleanupTimer.Dispose();
        foreach (var entry in _sessions.Values) entry.Dispose();
        _sessions.Clear();
        _disposed = true;
    }

    private class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => _semaphore.Release();
    }
}