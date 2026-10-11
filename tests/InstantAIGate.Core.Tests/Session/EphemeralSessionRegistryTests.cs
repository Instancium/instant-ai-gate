namespace InstantAIGate.Core.Tests.Session;

using InstantAIGate.Core.Services.Session;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

public sealed class EphemeralSessionRegistryTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly EphemeralSessionRegistry _registry;

    public EphemeralSessionRegistryTests()
    {
        _timeProvider = new FakeTimeProvider();
        _registry = new EphemeralSessionRegistry(_timeProvider);
    }

    [Fact]
    public void RegisterEphemeralSession_MultipleSessionsForSameConnection_TracksAll()
    {
        const string connectionId = "conn-1";
        _registry.RegisterEphemeralSession(connectionId, "session-1");
        _registry.RegisterEphemeralSession(connectionId, "session-2");

        var sessions = _registry.NotifyConnectionDisconnected(connectionId);

        Assert.Equal(new[] { "session-1", "session-2" }, sessions.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void NotifyConnectionDisconnected_RemovesSessionsFromRegistry()
    {
        const string connectionId = "conn-1";
        _registry.RegisterEphemeralSession(connectionId, "session-1");

        var firstCall = _registry.NotifyConnectionDisconnected(connectionId);
        var secondCall = _registry.NotifyConnectionDisconnected(connectionId);

        Assert.Single(firstCall, "session-1");
        Assert.Empty(secondCall);
    }

    [Fact]
    public void UnregisterSession_RemovesSessionFromAllConnections()
    {
        const string sessionId = "session-1";
        _registry.RegisterEphemeralSession("conn-1", sessionId);
        _registry.RegisterEphemeralSession("conn-2", sessionId);

        _registry.UnregisterSession(sessionId);

        Assert.Empty(_registry.NotifyConnectionDisconnected("conn-1"));
        Assert.Empty(_registry.NotifyConnectionDisconnected("conn-2"));
    }

    [Fact]
    public async Task Concurrency_SimultaneousRegisterAndUnregister_DoesNotThrow()
    {
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _registry.RegisterEphemeralSession($"conn-{i % 10}", $"session-{i}");
            _registry.UnregisterSession($"session-{i}");
        }));

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        Assert.Null(exception);
    }

    [Fact]
    public void DrainOrphanedSessions_ReturnsOnlySessionsOlderThanThreshold()
    {
        _registry.RegisterEphemeralSession("conn-1", "session-old");
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        _registry.RegisterEphemeralSession("conn-2", "session-new");

        var orphaned = _registry.DrainOrphanedSessions(TimeSpan.FromMinutes(2));

        Assert.Single(orphaned, "session-old");
        Assert.Single(_registry.NotifyConnectionDisconnected("conn-2"), "session-new");
    }
}