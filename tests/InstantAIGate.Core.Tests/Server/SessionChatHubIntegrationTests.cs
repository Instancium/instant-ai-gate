namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    [Fact]
    public async Task SessionChatHub_CanJoinSession_And_ReceiveTokens()
    {
   
        var adminClient = _fixture.CreateAdminClient();
        var loadRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/models/load");
        loadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        loadRequest.Content = JsonContent.Create(new { RepoId = _testRepoId });
        await adminClient.SendAsync(loadRequest);

        var hubUrl = new Uri(new Uri(_serverOptions.PublicBaseUrl), "/hub/chat");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(_tenantToken)!;
            })
            .Build();

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

       
        var completed = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(15))) == completionSource.Task;

        Assert.True(completed, "Timeout waiting for generation completion over SignalR.");
        Assert.NotEmpty(tokensReceived);
        Assert.Contains(tokensReceived, t => t.IsDone && t.FinishReason == "stop");

        await connection.StopAsync();
    }
}