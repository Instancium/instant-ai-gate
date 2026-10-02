namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class SessionInferenceManagerTests : IDisposable
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SessionInferenceManager _sessionManager;

    public SessionInferenceManagerTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _mockBackendFacade = new Mock<IBackendFacade>();
        _timeProvider = new FakeTimeProvider();

        _sessionManager = new SessionInferenceManager(
            _mockModelManager.Object,
            _mockBackendFacade.Object,
            NullLogger<SessionInferenceManager>.Instance,
            _timeProvider,
            idleTimeout: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task CreateSession_RegistersActiveSession()
    {
        var request = new SessionStartRequest("session-1", "test-repo");

        await _sessionManager.CreateSessionAsync(request);

        Assert.True(_sessionManager.TryGetSessionRepoId("session-1", out var repoId));
        Assert.Equal("test-repo", repoId);
    }

    [Fact]
    public async Task CreateSession_DuplicateSessionId_ThrowsInvalidOperationException()
    {
        var request = new SessionStartRequest("session-1", "test-repo");

        await _sessionManager.CreateSessionAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sessionManager.CreateSessionAsync(request));
    }

    [Fact]
    public async Task ReleaseSession_DisposesContextAndFreesSession()
    {
        var request = new SessionStartRequest("session-1", "test-repo");
        await _sessionManager.CreateSessionAsync(request);

        await _sessionManager.ReleaseSessionAsync("session-1");

        Assert.False(_sessionManager.TryGetSessionRepoId("session-1", out _));
    }

    [Fact]
    public async Task IdleTimeout_AutomaticallyReleasesContextAndEvictsSession()
    {
        var request = new SessionStartRequest("session-auto-evict", "test-repo");
        await _sessionManager.CreateSessionAsync(request);
        Assert.True(_sessionManager.TryGetSessionRepoId("session-auto-evict", out _));

        _timeProvider.Advance(TimeSpan.FromMinutes(6));
        await _sessionManager.CleanupIdleSessionsAsync();

        Assert.False(_sessionManager.TryGetSessionRepoId("session-auto-evict", out _));
    }

    [Fact]
    public async Task ConcurrencyGuard_PreventsParallelDeltaGenerationOnSameSession()
    {
        var request = new SessionStartRequest("session-lock", "test-repo");
        await _sessionManager.CreateSessionAsync(request);

        using var sessionGate = await _sessionManager.AcquireSessionExecutionGateAsync("session-lock");
        Assert.NotNull(sessionGate);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _sessionManager.AcquireSessionExecutionGateAsync("session-lock", cts.Token);
        });
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}