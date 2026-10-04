namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
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

        var act = async () => await userConnection.InvokeAsync<IEnumerable<ModelRegistryStatus>>("GetModelsAsync");
        await act.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task ControlPlane_WhenCalledByAdmin_Succeeds()
    {
        await using var adminConnection = CreateGatewayConnection(_adminToken);
        await adminConnection.StartAsync();

        var models = await adminConnection.InvokeAsync<IEnumerable<ModelRegistryStatus>>("GetModelsAsync");
        models.Should().NotBeNull();

        await adminConnection.InvokeAsync("SetQueueLimitAsync", 64);
        var queueMetrics = await adminConnection.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");
        queueMetrics.Should().NotBeNull();
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
        completed.Should().BeTrue("Inference tokens must stream over GatewayHub within timeout");
        tokensReceived.Should().NotBeEmpty();
        tokensReceived.Should().Contain(t => t.IsDone && t.FinishReason == "stop");

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
            received.Should().BeTrue("Hub must notify client of downloading status");

            var error = await errorTcs.Task;
            error.Should().Contain("downloading startup model 'download-target-model'");
            error.Should().Contain("42.5%");
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

        var actSubscribe = async () => await userConnection.InvokeAsync("SubscribeToModelDownload", _testRepoId);
        await actSubscribe.Should().NotThrowAsync();

        var actUnsubscribe = async () => await userConnection.InvokeAsync("UnsubscribeFromModelDownload", _testRepoId);
        await actUnsubscribe.Should().NotThrowAsync();
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
        received.Should().BeTrue("Connected client must receive initial GatewayStatusDetails snapshot");

        var snapshot = await statusTcs.Task;
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task Observability_GatewayStatus_ExplicitRpcCall_Succeeds()
    {
        await using var userConnection = CreateGatewayConnection(_tenantToken);
        await userConnection.StartAsync();

        var status = await userConnection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
        status.Should().NotBeNull();
        status.Status.Should().BeDefined();
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
        adminReceived.Should().BeTrue("Admin must receive metrics broadcast in GatewayAdminGroup");
        userMetricsReceived.Should().BeFalse("Standard user must not receive system metrics broadcast");
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

        receivedPacketsCount.Should().Be(1, "Event-driven broadcaster must not send redundant packets during idle state");
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
        completed.Should().BeTrue("Metrics must update immediately upon state mutation via event-driven signal");

        var updatedMetrics = await mutationReceivedTcs.Task;
        updatedMetrics.Should().NotBeNull();
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
        received.Should().BeTrue("Subscribed user should receive download progress updates over GatewayHub");

        var progressData = await userProgressTcs.Task;
        progressData.ModelId.Should().Be(_testRepoId);
    }

    #endregion
}