namespace InstantAIGate.Core.Tests.Inference;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class SessionInferenceMemoryManagementTests : IDisposable
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly Mock<IContextHandle> _mockContextHandle;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SessionInferenceManager _sessionManager;

    public SessionInferenceMemoryManagementTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _mockBackendFacade = new Mock<IBackendFacade>();
        _mockContextHandle = new Mock<IContextHandle>();
        _timeProvider = new FakeTimeProvider();

        var textModelContext = new ModelContext(_mockContextHandle.Object, (ptr, suppress) => { });
        var inferenceContext = new InferenceContext(textModelContext, visionContext: null);

        _mockModelManager
            .Setup(m => m.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inferenceContext);

        _sessionManager = new SessionInferenceManager(
            _mockModelManager.Object,
            _mockBackendFacade.Object,
            NullLogger<SessionInferenceManager>.Instance,
            _timeProvider,
            idleTimeout: TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task RollbackToPositionAsync_ValidState_DeletesSuffixAndTruncatesSequence()
    {
        const string sessionId = "rollback-session-ok";
        const string repoId = "test-repo";
        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, repoId));
        await _sessionManager.GetOrCreateContextAsync(sessionId);

        _sessionManager.UpdatePastTokensCount(sessionId, 50);
        _sessionManager.AppendSessionTokens(sessionId, [1, 2, 3, 4, 5]);

        _mockBackendFacade
            .Setup(b => b.RemoveContextMemoryRange(_mockContextHandle.Object, 0, 30, -1))
            .Returns(true);

        await _sessionManager.RollbackToPositionAsync(sessionId, 30);

        _sessionManager.GetPastTokensCount(sessionId).Should().Be(30);
        _mockBackendFacade.Verify(
            b => b.RemoveContextMemoryRange(_mockContextHandle.Object, 0, 30, -1),
            Times.Once);
    }

    [Fact]
    public async Task RollbackToPositionAsync_TargetExceedsPastTokens_ThrowsArgumentOutOfRangeException()
    {
        const string sessionId = "rollback-invalid-range";
        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, "test-repo"));
        _sessionManager.UpdatePastTokensCount(sessionId, 20);

        var act = () => _sessionManager.RollbackToPositionAsync(sessionId, 25);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("targetTokenPosition");
    }

    [Fact]
    public async Task ShiftMemoryRangeAsync_WhenCanShiftReturnsFalse_ThrowsNotSupportedException_AndPreservesCache()
    {
        const string sessionId = "shift-guard-active";
        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, "test-repo"));
        await _sessionManager.GetOrCreateContextAsync(sessionId);

        _sessionManager.UpdatePastTokensCount(sessionId, 100);
        _sessionManager.AppendSessionTokens(sessionId, [10, 20, 30, 40, 50]);

        _mockBackendFacade
            .Setup(b => b.CanShiftContextMemory(_mockContextHandle.Object))
            .Returns(false);

        var act = () => _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos: 10, count: 20);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*FlashAttention or GPU offload*");

        // Assert memory remained uncorrupted
        _sessionManager.GetPastTokensCount(sessionId).Should().Be(100);
        _mockBackendFacade.Verify(
            b => b.RemoveContextMemoryRange(It.IsAny<IContextHandle>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
        _mockBackendFacade.Verify(
            b => b.ShiftContextMemoryRange(It.IsAny<IContextHandle>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ShiftMemoryRangeAsync_WhenCanShiftReturnsTrue_ExecutesRemovalAndShiftAtomically()
    {
        const string sessionId = "shift-supported";
        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, "test-repo"));
        await _sessionManager.GetOrCreateContextAsync(sessionId);

        _sessionManager.UpdatePastTokensCount(sessionId, 100);
        _sessionManager.AppendSessionTokens(sessionId, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        _mockBackendFacade
            .Setup(b => b.CanShiftContextMemory(_mockContextHandle.Object))
            .Returns(true);

        _mockBackendFacade
            .Setup(b => b.RemoveContextMemoryRange(_mockContextHandle.Object, 0, 2, 6))
            .Returns(true);

        await _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos: 2, count: 4);

        _sessionManager.GetPastTokensCount(sessionId).Should().Be(96);

        _mockBackendFacade.Verify(
            b => b.RemoveContextMemoryRange(_mockContextHandle.Object, 0, 2, 6),
            Times.Once);

        _mockBackendFacade.Verify(
            b => b.ShiftContextMemoryRange(_mockContextHandle.Object, 0, 6, -1, -4),
            Times.Once);

        _sessionManager.GetSessionPrefixTokens(sessionId).Should().Equal([1, 2, 7, 8, 9, 10]);
    }

    [Fact]
    public async Task ShiftMemoryRangeAsync_OutOfBoundsRange_ThrowsArgumentOutOfRangeException()
    {
        const string sessionId = "shift-oor";
        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, "test-repo"));
        _sessionManager.UpdatePastTokensCount(sessionId, 50);

        var act = () => _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos: 40, count: 15);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("count");
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}