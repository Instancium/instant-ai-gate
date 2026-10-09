using InstantAIGate.Core.Interfaces.Session;
using System.Collections.Concurrent;

namespace InstantAIGate.Core.Services.Session;

/// <summary>
/// Thread-safe registry tracking ephemeral sessions per SignalR connection.
/// </summary>
public sealed class EphemeralSessionRegistry : IEphemeralSessionRegistry
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionToSessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessionRegistrationTime = new();
    private readonly TimeProvider _timeProvider;

    public EphemeralSessionRegistry(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void RegisterEphemeralSession(string connectionId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessions = _connectionToSessions.GetOrAdd(connectionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (sessions)
        {
            sessions.Add(sessionId);
        }
        _sessionRegistrationTime.TryAdd(sessionId, _timeProvider.GetUtcNow());
    }

    public void UnregisterSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _sessionRegistrationTime.TryRemove(sessionId, out _);

        foreach (var kvp in _connectionToSessions)
        {
            lock (kvp.Value)
            {
                kvp.Value.Remove(sessionId);
            }
        }
    }

    public IReadOnlyList<string> NotifyConnectionDisconnected(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        if (_connectionToSessions.TryRemove(connectionId, out var sessions))
        {
            lock (sessions)
            {
                foreach (var sessionId in sessions)
                {
                    _sessionRegistrationTime.TryRemove(sessionId, out _);
                }
                return sessions.ToArray();
            }
        }

        return Array.Empty<string>();
    }

    public IReadOnlyList<string> DrainOrphanedSessions(TimeSpan olderThan)
    {
        var cutoff = _timeProvider.GetUtcNow() - olderThan;
        var orphanedSessions = new List<string>();

        foreach (var kvp in _sessionRegistrationTime)
        {
            if (kvp.Value < cutoff)
            {
                orphanedSessions.Add(kvp.Key);
            }
        }

        foreach (var sessionId in orphanedSessions)
        {
            UnregisterSession(sessionId);
        }

        return orphanedSessions;
    }
}