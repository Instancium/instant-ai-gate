namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

public class SessionChatHubIntegrationTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private readonly TestServerOptions _serverOptions;
    private readonly string _tenantToken;
    private readonly string _adminToken;
    private readonly string _testRepoId;

    public SessionChatHubIntegrationTests(GatewayTestFixture fixture)
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
        var hubUrl = new Uri(new Uri(_serverOptions.PublicBaseUrl), "/hub/gateway");
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();
    }

    [Fact]
    public async Task SessionChatHub_CanJoinSession_And_ReceiveTokens()
    {
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        if (modelManager.GetActiveSettings() == null)
        {
            await using var adminConn = CreateGatewayConnection(_adminToken);
            await adminConn.StartAsync();
            await adminConn.InvokeAsync("LoadModelAsync", _testRepoId, "Default");
        }

        await using var connection = CreateGatewayConnection(_tenantToken);
        var tokensReceived = new List<SessionTokenDelta>();
        var completionSource = new TaskCompletionSource();

        connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            tokensReceived.Add(delta);
            if (delta.IsDone)
            {
                completionSource.TrySetResult();
            }
        });

        await connection.StartAsync();

        string sessionId = $"test-session-{Guid.NewGuid():N}";
        await connection.InvokeAsync("JoinSession", sessionId, _testRepoId);

        var deltaMessage = new ChatMessage("user", "Hello, SignalR!");
        await connection.InvokeAsync("SendPromptDelta", sessionId, deltaMessage);

        var completed = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(20))) == completionSource.Task;
        completed.Should().BeTrue("Timeout waiting for generation completion over GatewayHub.");

        tokensReceived.Should().NotBeEmpty();
        tokensReceived.Should().Contain(t => t.IsDone && t.FinishReason == "stop");

        await connection.InvokeAsync("LeaveSession", sessionId);
        await connection.StopAsync();
    }
}