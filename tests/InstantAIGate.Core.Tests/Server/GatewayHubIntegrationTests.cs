namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
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
        var gatewayUrl = new Uri(new Uri(_serverOptions.PublicBaseUrl), "/hub/gateway");
        return new HubConnectionBuilder()
            .WithUrl(gatewayUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();
    }

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

        var prompt = new ChatMessage("user", "Hello via GatewayHub!");
        await userConnection.InvokeAsync("SendPromptDelta", sessionId, prompt);

        var completed = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(20))) == completionSource.Task;
        completed.Should().BeTrue("Inference tokens must stream over GatewayHub within timeout");
        tokensReceived.Should().NotBeEmpty();
        tokensReceived.Should().Contain(t => t.IsDone && t.FinishReason == "stop");

        await userConnection.InvokeAsync("LeaveSession", sessionId);
    }

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
}