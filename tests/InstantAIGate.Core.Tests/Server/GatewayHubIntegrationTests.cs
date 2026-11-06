namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class GatewayHubIntegrationTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private readonly TestServerOptions _serverOptions;
    private readonly string _tenantToken;
    private readonly string _adminToken;
    private readonly string _testRepoId;

    public GatewayHubIntegrationTests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
        _serverOptions = fixture.ServerOptions;
        _tenantToken = _serverOptions.TenantApiKey;
        _adminToken = _serverOptions.AdminApiKey;
        _testRepoId = fixture.ModelOptions.RepoId;
        NativeLibraryLoader.Load();
    }

    private HubConnection CreateGatewayConnection(string token)
    {
        return new HubConnectionBuilder()
            .WithUrl(_fixture.GatewayHubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();
    }

    #region Control Plane Tests

    [Fact]
    public async Task ControlPlane_WhenCalledByTenantUser_ThrowsHubExceptionOrUnauthorized()
    {
        await using var userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();

        await Assert.ThrowsAsync<HubException>(
            () => userConnection.InvokeAsync<IEnumerable<ModelRegistryStatus>>("GetModelsAsync"));
    }

    [Fact]
    public async Task ControlPlane_WhenCalledByAdmin_Succeeds()
    {
        await using var adminConnection = CreateGatewayConnection(_adminToken);
        await adminConnection.StartAsync();

        var models = await adminConnection.InvokeAsync<IEnumerable<ModelRegistryStatus>>("GetModelsAsync");
        Assert.NotNull(models);

        await adminConnection.InvokeAsync("SetQueueLimitAsync", 64);
        var queueMetrics = await adminConnection.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");
        Assert.NotNull(queueMetrics);
    }

    #endregion

    #region Data Plane Tests

    [Fact]
    public async Task DataPlane_TenantUser_CanJoinSession_And_ReceiveDeltaTokens()
    {
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        if (modelManager.GetActiveSettings() == null)
        {
            await using var adminConn = CreateGatewayConnection(_adminToken);
            await adminConn.StartAsync();
            await adminConn.InvokeAsync("LoadModelAsync", _testRepoId, "Default");
        }

        await using var userConnection = CreateGatewayConnection(_tenantToken);
        var tokensReceived = new List<SessionTokenDelta>();
        var completionSource = new TaskCompletionSource();

        userConnection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            tokensReceived.Add(delta);
            if (delta.IsDone)
            {
                completionSource.TrySetResult();
            }
        });

        await userConnection.StartAsync();
        string sessionId = $"gw-session-{Guid.NewGuid():N}";
        await userConnection.InvokeAsync("JoinSession", sessionId, _testRepoId);

        var prompt = new ChatMessage("user", "Hello via unified GatewayHub!");
        await userConnection.InvokeAsync("SendPromptDelta", sessionId, prompt);

        var completed = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(20))) == completionSource.Task;
        Assert.True(completed, "Inference tokens must stream over GatewayHub within timeout");
        Assert.NotEmpty(tokensReceived);
        Assert.Contains(tokensReceived, t => t.IsDone && t.FinishReason == "stop");

        await userConnection.InvokeAsync("LeaveSession", sessionId);
    }

    [Fact]
    public async Task DataPlane_JoinSession_WhenModelDownloading_ReturnsInformativeProgressError()
    {
        var stateManager = _fixture.Services.GetRequiredService<IGatewayStateManager>();
        stateManager.SetDownloading("download-target-model", 42.5f, 42500, 100000, 10000, "Downloading test model");

        try
        {
            await using var userConnection = CreateGatewayConnection(_tenantToken);
            var errorTcs = new TaskCompletionSource<string>();
            userConnection.On<string>("ReceiveError", err =>
            {
                errorTcs.TrySetResult(err);
            });

            await userConnection.StartAsync();
            await userConnection.InvokeAsync("JoinSession", "session-downloading-test", string.Empty);

            var received = await Task.WhenAny(errorTcs.Task, Task.Delay(TimeSpan.FromSeconds(3))) == errorTcs.Task;
            Assert.True(received, "Hub must notify client of downloading status");

            var error = await errorTcs.Task;
            Assert.Contains("downloading startup model 'download-target-model'", error);
            Assert.Contains("42.5%", error);
        }
        finally
        {
            stateManager.Reset();
        }
    }

    #endregion

    #region User & System Observability Tests

    [Fact]
    public async Task Observability_DownloadGroup_Subscription_IsCallable()
    {
        await using var userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();

        var subscribeEx = await Record.ExceptionAsync(
            () => userConnection.InvokeAsync("SubscribeToModelDownload", _testRepoId));
        Assert.Null(subscribeEx);

        var unsubscribeEx = await Record.ExceptionAsync(
            () => userConnection.InvokeAsync("UnsubscribeFromModelDownload", _testRepoId));
        Assert.Null(unsubscribeEx);
    }

    [Fact]
    public async Task Observability_GatewayStatus_SnapshotReceivedOnConnected()
    {
        await using var userConnection = CreateGatewayConnection(_tenantToken);
        var statusTcs = new TaskCompletionSource<GatewayStatusDetails>();

        userConnection.On<GatewayStatusDetails>("ReceiveGatewayStatus", s =>
        {
            statusTcs.TrySetResult(s);
        });

        await userConnection.StartAsync();

        var received = await Task.WhenAny(statusTcs.Task, Task.Delay(TimeSpan.FromSeconds(3))) == statusTcs.Task;
        Assert.True(received, "Connected client must receive initial GatewayStatusDetails snapshot");

        var snapshot = await statusTcs.Task;
        Assert.NotNull(snapshot);
    }

    [Fact]
    public async Task Observability_GatewayStatus_ExplicitRpcCall_Succeeds()
    {
        await using var userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();

        var status = await userConnection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");

        Assert.NotNull(status);
        Assert.True(Enum.IsDefined(status.Status));
    }

    [Fact]
    public async Task Observability_SystemMetricsAndLogs_DeliveredOnlyToAdmin()
    {
        await using var adminConn = CreateGatewayConnection(_adminToken);
        await using var userConn = CreateGatewayConnection(_tenantToken);

        var adminMetricsTcs = new TaskCompletionSource<bool>();
        var userMetricsReceived = false;

        adminConn.On<InferenceMetrics, object>("ReceiveMetrics", (m, d) =>
        {
            adminMetricsTcs.TrySetResult(true);
        });

        userConn.On<InferenceMetrics, object>("ReceiveMetrics", (m, d) =>
        {
            userMetricsReceived = true;
        });

        await adminConn.StartAsync();
        await userConn.StartAsync();

        var adminReceived = await Task.WhenAny(adminMetricsTcs.Task, Task.Delay(TimeSpan.FromSeconds(3))) == adminMetricsTcs.Task;
        Assert.True(adminReceived, "Admin must receive metrics broadcast in GatewayAdminGroup");
        Assert.False(userMetricsReceived, "Standard user must not receive system metrics broadcast");
    }

    [Fact]
    public async Task Observability_IdleState_DoesNotProduceRedundantBroadcasts()
    {
        await using var adminConn = CreateGatewayConnection(_adminToken);
        int receivedPacketsCount = 0;

        adminConn.On<InferenceMetrics, object>("ReceiveMetrics", (_, _) =>
        {
            Interlocked.Increment(ref receivedPacketsCount);
        });

        await adminConn.StartAsync();
        await Task.Delay(2500);

        Assert.Equal(1, receivedPacketsCount);
    }

    [Fact]
    public async Task Observability_StateMutation_PushesMetricsImmediately()
    {
        await using var adminConn = CreateGatewayConnection(_adminToken);

        var initialReceivedTcs = new TaskCompletionSource<bool>();
        var mutationReceivedTcs = new TaskCompletionSource<InferenceMetrics>();

        adminConn.On<InferenceMetrics, object>("ReceiveMetrics", (m, _) =>
        {
            if (!initialReceivedTcs.Task.IsCompleted)
            {
                initialReceivedTcs.TrySetResult(true);
            }
            else
            {
                mutationReceivedTcs.TrySetResult(m);
            }
        });

        await adminConn.StartAsync();
        await initialReceivedTcs.Task;

        await adminConn.InvokeAsync("SetQueueLimitAsync", 42);

        var completed = await Task.WhenAny(mutationReceivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(2))) == mutationReceivedTcs.Task;
        Assert.True(completed, "Metrics must update immediately upon state mutation via event-driven signal");

        var updatedMetrics = await mutationReceivedTcs.Task;
        Assert.NotNull(updatedMetrics);
    }

    [Fact]
    public async Task Observability_ModelDownloadProgress_BroadcastsToSubscribedUser()
    {
        await using var userConn = CreateGatewayConnection(_tenantToken);
        await using var adminConn = CreateGatewayConnection(_adminToken);

        await userConn.StartAsync();
        await adminConn.StartAsync();

        await userConn.InvokeAsync("SubscribeToModelDownload", _testRepoId);

        var userProgressTcs = new TaskCompletionSource<DownloadProgress>();
        userConn.On<DownloadProgress>("ReceiveDownloadProgress", p =>
        {
            userProgressTcs.TrySetResult(p);
        });

        await adminConn.InvokeAsync("DownloadModelAsync", _testRepoId);

        var received = await Task.WhenAny(userProgressTcs.Task, Task.Delay(TimeSpan.FromSeconds(5))) == userProgressTcs.Task;
        Assert.True(received, "Subscribed user should receive download progress updates over GatewayHub");

        var progressData = await userProgressTcs.Task;
        Assert.Equal(_testRepoId, progressData.ModelId);
    }

    [Fact]
    public async Task Observability_AdminCanRequestDashboardSnapshot_And_ReceiveHardwareAndSessionMetrics()
    {
        await using HubConnection userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();
        string sessionId = $"test-session-{Guid.NewGuid():N}";
        await userConnection.InvokeAsync("JoinSession", sessionId, _testRepoId);

        await using HubConnection adminConnection = CreateGatewayConnection(_adminToken);
        await adminConnection.StartAsync();

        DashboardTelemetrySnapshot snapshot =
            await adminConnection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync");

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Hardware.ProcessRamBytes > 0);
        Assert.True(snapshot.Hardware.SystemTotalRamBytes > 0);
        Assert.NotNull(snapshot.GatewayStatus);
        Assert.NotNull(snapshot.InferenceMetrics);
        Assert.NotNull(snapshot.Models);
        Assert.Contains(snapshot.Sessions, s => s.SessionId == sessionId);

        await using HubConnection tenantConnection = CreateGatewayConnection(_tenantToken);
        await tenantConnection.StartAsync();
        await Assert.ThrowsAsync<HubException>(
            () => tenantConnection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync"));

        await userConnection.InvokeAsync("LeaveSession", sessionId);
    }

    [Fact]
    public async Task Observability_SubscribedAdmin_ReceivesPeriodicDashboardTelemetry()
    {
        var received = new List<DashboardTelemetrySnapshot>();
        using var tickGate = new SemaphoreSlim(initialCount: 0);

        await using HubConnection adminConnection = CreateGatewayConnection(_adminToken);
        adminConnection.On<DashboardTelemetrySnapshot>("ReceiveDashboardTelemetry", snapshot =>
        {
            lock (received)
            {
                received.Add(snapshot);
            }

            tickGate.Release();
        });

        await adminConnection.StartAsync();
        await adminConnection.InvokeAsync("SubscribeToDashboardTelemetry");

        for (int i = 0; i < 3; i++)
        {
            bool arrived = await tickGate.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(arrived, $"Expected periodic dashboard telemetry tick #{i + 1} within 5 s (1 Hz cadence).");
        }

        lock (received)
        {
            Assert.True(received.Count >= 3);
            Assert.All(received, s => Assert.True(s.Hardware.ProcessRamBytes > 0));
        }

        int tenantTicks = 0;
        await using HubConnection tenantConnection = CreateGatewayConnection(_tenantToken);
        tenantConnection.On<DashboardTelemetrySnapshot>("ReceiveDashboardTelemetry", _ => Interlocked.Increment(ref tenantTicks));
        await tenantConnection.StartAsync();

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.Equal(0, Volatile.Read(ref tenantTicks));
    }

    [Fact]
    public async Task Observability_EndToEnd_TelemetryLifecycle_TracksSessionGenerationAndSlotCleanup()
    {
        await using HubConnection adminConnection = CreateGatewayConnection(_adminToken);
        var telemetrySnapshots = new List<DashboardTelemetrySnapshot>();
        adminConnection.On<DashboardTelemetrySnapshot>("ReceiveDashboardTelemetry", snap =>
        {
            lock (telemetrySnapshots)
            {
                telemetrySnapshots.Add(snap);
            }
        });

        await adminConnection.StartAsync();
        await adminConnection.InvokeAsync("SubscribeToDashboardTelemetry");

        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        if (modelManager.GetActiveSettings(_testRepoId) == null)
        {
            await adminConnection.InvokeAsync("LoadModelAsync", _testRepoId, "Default");
        }

        await using HubConnection userConnection = CreateGatewayConnection(_tenantToken);
        var generationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedTokens = new List<SessionTokenDelta>();

        string sessionId = $"e2e-telemetry-session-{Guid.NewGuid():N}";
        userConnection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            if (delta.SessionId == sessionId)
            {
                receivedTokens.Add(delta);
                if (delta.IsDone)
                {
                    generationCompleted.TrySetResult();
                }
            }
        });

        await userConnection.StartAsync();
        await userConnection.InvokeAsync("JoinSession", sessionId, _testRepoId);

        var prompt = new ChatMessage("user", "Say Pong.");
        await userConnection.InvokeAsync("SendPromptDelta", sessionId, prompt);

        bool finishedInTime = await Task.WhenAny(generationCompleted.Task, Task.Delay(TimeSpan.FromSeconds(30))) == generationCompleted.Task;
        Assert.True(finishedInTime, "Token generation must complete within timeout.");
        Assert.NotEmpty(receivedTokens);

        DashboardTelemetrySnapshot activeSnapshot =
            await adminConnection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync");

        Assert.NotNull(activeSnapshot);
        Assert.True(activeSnapshot.Hardware.ProcessRamBytes > 0);
        Assert.True(activeSnapshot.Hardware.ManagedHeapBytes > 0);

        var modelTelemetry = Assert.Single(activeSnapshot.Models, m =>
            string.Equals(m.RepoId, _testRepoId, StringComparison.OrdinalIgnoreCase));
        Assert.True(modelTelemetry.IsLoaded);
        Assert.True(modelTelemetry.ModelWeightsBytes > 0);
        Assert.True(modelTelemetry.ActiveLeases >= 1, "Model must report at least 1 active context lease while session holds slot.");

        var sessionTelemetry = Assert.Single(activeSnapshot.Sessions, s => s.SessionId == sessionId);
        Assert.True(sessionTelemetry.PastTokens > 0, "Session PastTokens must be greater than zero after prompt + generation.");
        Assert.True(sessionTelemetry.EvalTps > 0.0, "Session EvalTps must be recorded and greater than zero.");
        Assert.True(sessionTelemetry.PromptTps > 0.0, "Session PromptTps must be recorded and greater than zero.");
        Assert.True(sessionTelemetry.HasAllocatedContext, "Session must hold an allocated native context.");

        await adminConnection.InvokeAsync("ForceKillSessionAsync", sessionId);
        await adminConnection.InvokeAsync("PurgeIdleContextsAsync", _testRepoId);

        DashboardTelemetrySnapshot cleanedSnapshot =
            await adminConnection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync");

        Assert.DoesNotContain(cleanedSnapshot.Sessions, s => s.SessionId == sessionId);
        var cleanedModelTelemetry = Assert.Single(cleanedSnapshot.Models, m =>
            string.Equals(m.RepoId, _testRepoId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, cleanedModelTelemetry.ActiveLeases);
        Assert.Equal(0, cleanedModelTelemetry.IdlePooledContexts);
    }

    [Fact]
    public async Task ControlPlane_AdminCanForceKillHangingSession()
    {
        await using HubConnection userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();
        string sessionId = $"test-kill-session-{Guid.NewGuid():N}";
        await userConnection.InvokeAsync("JoinSession", sessionId, _testRepoId);

        await using HubConnection adminConnection = CreateGatewayConnection(_adminToken);
        await adminConnection.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => userConnection.InvokeAsync("ForceKillSessionAsync", sessionId));

        await adminConnection.InvokeAsync("ForceKillSessionAsync", sessionId);

        IReadOnlyList<SessionTelemetryDto> remaining =
            await adminConnection.InvokeAsync<IReadOnlyList<SessionTelemetryDto>>("GetActiveSessionsAsync");
        Assert.DoesNotContain(remaining, s => s.SessionId == sessionId);

        DashboardTelemetrySnapshot snapshot =
            await adminConnection.InvokeAsync<DashboardTelemetrySnapshot>("GetDashboardSnapshotAsync");
        Assert.All(snapshot.Models, m => Assert.Equal(0, m.ActiveLeases));
    }

    #endregion
}