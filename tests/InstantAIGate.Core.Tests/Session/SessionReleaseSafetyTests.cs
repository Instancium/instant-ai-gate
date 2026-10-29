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

public sealed class SessionReleaseSafetyTests : IDisposable
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SessionInferenceManager _sessionManager;

    public SessionReleaseSafetyTests()
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
    public async Task ReleaseSession_WaitsForExecutionGate_BeforeCompleting()
    {
        var request = new SessionStartRequest("session-gate", "test-repo");
        await _sessionManager.CreateSessionAsync(request);

        // Acquire the gate (simulating in-flight llama_decode)
        var gate = await _sessionManager.AcquireSessionExecutionGateAsync("session-gate");

        // Start release in parallel
        var releaseTask = _sessionManager.ReleaseSessionAsync("session-gate", destroySlot: false);

        // Give it a moment to start waiting
        await Task.Delay(100);

        // Release should NOT be complete yet (gate is held)
        Assert.False(releaseTask.IsCompleted);

        // Release the gate
        gate.Dispose();

        // Now release should complete
        await releaseTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(releaseTask.IsCompleted);
    }

    [Fact]
    public async Task ReleaseSession_WhenSessionDoesNotExist_CompletesGracefully()
    {
        var exception = await Record.ExceptionAsync(
            () => _sessionManager.ReleaseSessionAsync("nonexistent-session"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task IdleCleanup_OnlyEvictsSessionsBeyondTimeout()
    {
        var request = new SessionStartRequest("session-idle-test", "test-repo");
        await _sessionManager.CreateSessionAsync(request);

        // Advance past idle timeout
        _timeProvider.Advance(TimeSpan.FromMinutes(11));

        await _sessionManager.CleanupIdleSessionsAsync();

        Assert.False(_sessionManager.TryGetSessionRepoId("session-idle-test", out _));
    }

    [Fact]
    public async Task IdleCleanup_DoesNotEvictRecentlyTouchedSessions()
    {
        var request = new SessionStartRequest("session-active", "test-repo");
        await _sessionManager.CreateSessionAsync(request);

        // Advance 9 minutes, then touch
        _timeProvider.Advance(TimeSpan.FromMinutes(9));
        _sessionManager.UpdatePastTokensCount("session-active", 50);

        // Advance another 5 minutes (total 14 from creation, but only 5 from touch)
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await _sessionManager.CleanupIdleSessionsAsync();

        // Session should still be alive (only 5 min since last touch)
        Assert.True(_sessionManager.TryGetSessionRepoId("session-active", out _));
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}
