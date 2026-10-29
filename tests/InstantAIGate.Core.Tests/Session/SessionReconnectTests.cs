namespace InstantAIGate.Core.Tests.Session;

using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

public sealed class SessionReconnectTests : IDisposable
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SessionInferenceManager _sessionManager;

    public SessionReconnectTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _mockBackendFacade = new Mock<IBackendFacade>();
        _timeProvider = new FakeTimeProvider();
        _sessionManager = new SessionInferenceManager(
            _mockModelManager.Object,
            _mockBackendFacade.Object,
            NullLogger<SessionInferenceManager>.Instance,
            _timeProvider,
            idleTimeout: TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task CreateSession_WhenSessionAlreadyExists_ResumesWithoutException()
    {
        var request = new SessionStartRequest("session-resume", "test-repo");

        await _sessionManager.CreateSessionAsync(request);
        Assert.True(_sessionManager.TryGetSessionRepoId("session-resume", out _));

        // Simulate activity
        _sessionManager.UpdatePastTokensCount("session-resume", 100);
        _sessionManager.AppendSessionTokens("session-resume", [1, 2, 3, 4, 5]);

        // Simulate reconnect: call CreateSessionAsync again with same sessionId
        var exception = await Record.ExceptionAsync(
            () => _sessionManager.CreateSessionAsync(request));

        Assert.Null(exception);

        // Verify KV-cache state is preserved
        Assert.Equal(100, _sessionManager.GetPastTokensCount("session-resume"));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, _sessionManager.GetSessionPrefixTokens("session-resume"));
    }

    [Fact]
    public async Task CreateSession_WhenSessionExists_UpdatesLastAccessed()
    {
        var request = new SessionStartRequest("session-touch", "test-repo");

        await _sessionManager.CreateSessionAsync(request);

        // Advance time
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Reconnect
        await _sessionManager.CreateSessionAsync(request);

        // Advance another 5 minutes (total 10 from creation, but only 5 from reconnect)
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Session should still be alive (idle timeout is 10 min from last touch)
        await _sessionManager.CleanupIdleSessionsAsync();
        Assert.True(_sessionManager.TryGetSessionRepoId("session-touch", out _));
    }

    [Fact]
    public async Task CreateSession_WhenNew_CreatesSuccessfully()
    {
        var request = new SessionStartRequest("session-new", "test-repo");

        await _sessionManager.CreateSessionAsync(request);

        Assert.True(_sessionManager.TryGetSessionRepoId("session-new", out var repoId));
        Assert.Equal("test-repo", repoId);
    }

    [Fact]
    public async Task DisconnectGracePeriod_WhenClientReconnectsWithinGracePeriod_PreservesSession()
    {
        var request = new SessionStartRequest("session-grace", "test-repo");
        await _sessionManager.CreateSessionAsync(request, connectionId: "conn-1");

        // Set token state
        _sessionManager.UpdatePastTokensCount("session-grace", 42);

        // Connection drops
        _sessionManager.NotifyConnectionDisconnected("conn-1");

        // Advance 15 seconds (less than 30s grace period)
        _timeProvider.Advance(TimeSpan.FromSeconds(15));

        // Client reconnects with new connectionId
        await _sessionManager.CreateSessionAsync(request, connectionId: "conn-2");

        // Advance past original grace period (another 20 seconds, total 35s)
        _timeProvider.Advance(TimeSpan.FromSeconds(20));

        // Session must still be alive and tokens preserved
        Assert.True(_sessionManager.TryGetSessionRepoId("session-grace", out _));
        Assert.Equal(42, _sessionManager.GetPastTokensCount("session-grace"));
    }

    [Fact]
    public async Task DisconnectGracePeriod_WhenClientDoesNotReconnect_EvictsSession()
    {
        var request = new SessionStartRequest("session-evict-grace", "test-repo");
        await _sessionManager.CreateSessionAsync(request, connectionId: "conn-dropped");

        // Connection drops
        _sessionManager.NotifyConnectionDisconnected("conn-dropped");

        // Advance beyond 30s grace period
        _timeProvider.Advance(TimeSpan.FromSeconds(35));

        // Allow any pending async continuations to run
        await Task.Delay(50);

        // Session must be evicted
        Assert.False(_sessionManager.TryGetSessionRepoId("session-evict-grace", out _));
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}
