namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class SessionTelemetryInspectionTests : IDisposable
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly Mock<IModelProvider> _mockModelProvider;
    private readonly Mock<IModelLocator> _mockModelLocator;
    private readonly Mock<IQueueManager> _mockQueueManager;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SessionInferenceManager _sessionManager;

    public SessionTelemetryInspectionTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _mockBackendFacade = new Mock<IBackendFacade>();
        _mockModelProvider = new Mock<IModelProvider>();
        _mockModelLocator = new Mock<IModelLocator>();
        _mockQueueManager = new Mock<IQueueManager>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

        _mockModelManager
            .Setup(m => m.GetModelSettings(It.IsAny<string>()))
            .Returns(new ModelSettings { RepoId = "test-repo", ContextSize = 4096 });

        _sessionManager = new SessionInferenceManager(
            _mockModelManager.Object,
            _mockBackendFacade.Object,
            NullLogger<SessionInferenceManager>.Instance,
            _timeProvider,
            idleTimeout: TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetSessionsTelemetry_NewSession_ReturnsIdleState()
    {
        var request = new SessionStartRequest("sess-idle", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-1");

        var telemetry = _sessionManager.GetSessionsTelemetry();

        Assert.Single(telemetry);
        var session = telemetry[0];
        Assert.Equal("sess-idle", session.SessionId);
        Assert.Equal("test-repo", session.RepoId);
        Assert.Equal(SessionStateType.Idle, session.State);
        Assert.Equal(0, session.PastTokens);
        Assert.Equal(4096, session.MaxContextTokens);
        Assert.Equal("conn-1", session.BoundConnectionId);
        Assert.False(session.HasAllocatedContext);
        Assert.Equal(0.0, session.IdleSeconds, precision: 1);
    }

    [Fact]
    public async Task GetSessionsTelemetry_WhenGateHeld_ReturnsGeneratingState()
    {
        var request = new SessionStartRequest("sess-gen", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-2");

        using var gate = await _sessionManager.AcquireSessionExecutionGateAsync("sess-gen");

        var telemetry = _sessionManager.GetSessionsTelemetry();

        Assert.Single(telemetry);
        Assert.Equal(SessionStateType.Generating, telemetry[0].State);
    }

    [Fact]
    public async Task GetSessionsTelemetry_AfterDisconnect_ReturnsGracePeriodState()
    {
        var request = new SessionStartRequest("sess-grace", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-3");

        _sessionManager.NotifyConnectionDisconnected("conn-3");

        var telemetry = _sessionManager.GetSessionsTelemetry();

        Assert.Single(telemetry);
        Assert.Equal(SessionStateType.DisconnectedGracePeriod, telemetry[0].State);
        Assert.Null(telemetry[0].BoundConnectionId);
    }

    [Fact]
    public async Task GetSessionsTelemetry_AfterReconnect_ReturnsIdleAgain()
    {
        var request = new SessionStartRequest("sess-reconnect", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-4");

        _sessionManager.NotifyConnectionDisconnected("conn-4");

        var duringGrace = _sessionManager.GetSessionsTelemetry();
        Assert.Equal(SessionStateType.DisconnectedGracePeriod, duringGrace[0].State);

        await _sessionManager.CreateSessionAsync(request, "conn-5");

        var afterReconnect = _sessionManager.GetSessionsTelemetry();
        Assert.Equal(SessionStateType.Idle, afterReconnect[0].State);
        Assert.Equal("conn-5", afterReconnect[0].BoundConnectionId);
    }

    [Fact]
    public async Task UpdateSessionPerformance_StoresTpsInTelemetry()
    {
        var request = new SessionStartRequest("sess-perf", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-6");

        _sessionManager.UpdateSessionPerformance("sess-perf", promptTps: 250.0, evalTps: 45.5);

        var telemetry = _sessionManager.GetSessionsTelemetry();
        Assert.Single(telemetry);
        Assert.Equal(250.0, telemetry[0].PromptTps);
        Assert.Equal(45.5, telemetry[0].EvalTps);
    }

    [Fact]
    public async Task GetSessionsTelemetry_IdleSeconds_IncreasesOverTime()
    {
        var request = new SessionStartRequest("sess-time", "test-repo");
        await _sessionManager.CreateSessionAsync(request, "conn-7");

        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        var telemetry = _sessionManager.GetSessionsTelemetry();
        Assert.Single(telemetry);
        Assert.True(telemetry[0].IdleSeconds >= 29.0, $"Expected >= 29s idle, got {telemetry[0].IdleSeconds}");
    }

    [Fact]
    public async Task GetSessionsTelemetry_MultipleSessions_ReturnsAll()
    {
        await _sessionManager.CreateSessionAsync(new SessionStartRequest("s1", "test-repo"), "c1");
        await _sessionManager.CreateSessionAsync(new SessionStartRequest("s2", "test-repo"), "c2");
        await _sessionManager.CreateSessionAsync(new SessionStartRequest("s3", "test-repo"), "c3");

        var telemetry = _sessionManager.GetSessionsTelemetry();

        Assert.Equal(3, telemetry.Count);
        Assert.Contains(telemetry, t => t.SessionId == "s1");
        Assert.Contains(telemetry, t => t.SessionId == "s2");
        Assert.Contains(telemetry, t => t.SessionId == "s3");
    }

    [Fact]
    public async Task GetSessionsTelemetry_ReleasedSession_NotIncluded()
    {
        await _sessionManager.CreateSessionAsync(new SessionStartRequest("sess-gone", "test-repo"), "conn-x");
        await _sessionManager.ReleaseSessionAsync("sess-gone");

        var telemetry = _sessionManager.GetSessionsTelemetry();

        Assert.Empty(telemetry);
    }

    [Fact]
    public async Task ModelManager_GetModelsPoolTelemetry_ReturnsCorrectLeaseAndPoolCounts()
    {
        var modelProvider = new Mock<IModelProvider>();
        var modelLocator = new Mock<IModelLocator>();
        var queueManager = new Mock<IQueueManager>();

        modelLocator
            .Setup(l => l.ResolvePathsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedModelPaths("/fake/model.gguf", null, 1024));

        modelProvider
            .Setup(p => p.InitializeAsync(It.IsAny<ModelSettings>(), It.IsAny<ResolvedModelPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        modelProvider
            .Setup(p => p.GetNativeDetails())
            .Returns(new List<NativeModelDetails>
            {
                new()
                {
                    RepoId = "test-model",
                    ContextSize = 4096,
                    GpuLayers = 99,
                    TotalLayers = 36,
                    IdleContextsCount = 2,
                    Backend = "llama.cpp"
                }
            });

        modelProvider
            .Setup(p => p.GetInferenceContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var handle = new Mock<IContextHandle>().Object;
                var textCtx = new ModelContext(handle, (_, _) => { });
                return new InferenceContext(textCtx, null);
            });

        var manager = new ModelManager(
            modelProvider.Object,
            modelLocator.Object,
            queueManager.Object,
            NullLogger<ModelManager>.Instance);

        var config = new ModelSettings
        {
            RepoId = "test-model",
            MaxContexts = 4,
            GpuLayerCount = 99,
            ContextSize = 4096,
            KvCacheQuantization = "Q8_0",
            Type = ModelType.Llm
        };

        await manager.LoadModelAsync(config);

        var telemetryBeforeLease = manager.GetModelsPoolTelemetry();
        Assert.Single(telemetryBeforeLease);
        Assert.Equal(0, telemetryBeforeLease[0].ActiveLeases);
        Assert.Equal(2, telemetryBeforeLease[0].IdlePooledContexts);
        Assert.Equal(4, telemetryBeforeLease[0].MaxContexts);
        Assert.Equal(99, telemetryBeforeLease[0].GpuLayers);
        Assert.Equal(36, telemetryBeforeLease[0].TotalLayers);
        Assert.Equal("Q8_0", telemetryBeforeLease[0].KvCacheQuantization);
        Assert.False(telemetryBeforeLease[0].IsDraining);
        Assert.True(telemetryBeforeLease[0].IsLoaded);

        using var lease = await manager.AcquireContextAsync("test-model");

        var telemetryAfterLease = manager.GetModelsPoolTelemetry();
        Assert.Equal(1, telemetryAfterLease[0].ActiveLeases);

        manager.Dispose();
    }

    [Fact]
    public async Task ModelManager_GetModelsPoolTelemetry_MultipleModels_ReturnsAll()
    {
        var modelProvider = new Mock<IModelProvider>();
        var modelLocator = new Mock<IModelLocator>();
        var queueManager = new Mock<IQueueManager>();

        modelLocator
            .Setup(l => l.ResolvePathsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string repoId, bool _, CancellationToken _) =>
                new ResolvedModelPaths("/fake/" + repoId + ".gguf", null, 1024));

        modelProvider
            .Setup(p => p.InitializeAsync(It.IsAny<ModelSettings>(), It.IsAny<ResolvedModelPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        modelProvider
            .Setup(p => p.GetNativeDetails())
            .Returns(new List<NativeModelDetails>
            {
                new() { RepoId = "model-a", TotalLayers = 32, IdleContextsCount = 1 },
                new() { RepoId = "model-b", TotalLayers = 28, IdleContextsCount = 0 }
            });

        var manager = new ModelManager(
            modelProvider.Object,
            modelLocator.Object,
            queueManager.Object,
            NullLogger<ModelManager>.Instance);

        await manager.LoadModelAsync(new ModelSettings { RepoId = "model-a", MaxContexts = 2, Type = ModelType.Llm });
        await manager.LoadModelAsync(new ModelSettings { RepoId = "model-b", MaxContexts = 4, Type = ModelType.Embedding, Embeddings = true });

        var telemetry = manager.GetModelsPoolTelemetry();

        Assert.Equal(2, telemetry.Count);
        Assert.Contains(telemetry, t => t.RepoId == "model-a" && t.MaxContexts == 2);
        Assert.Contains(telemetry, t => t.RepoId == "model-b" && t.MaxContexts == 4 && t.Type == ModelType.Embedding);

        manager.Dispose();
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}
