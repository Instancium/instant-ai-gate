namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

public class GatewayInferenceE2ETests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private static readonly TestModelOptions ModelOptions = TestConfig.Model;

    public GatewayInferenceE2ETests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
        InstantAIGate.Native.Bindings.NativeLibraryLoader.Load();
    }

    private HubConnection CreateGatewayConnection(string token)
    {
        var gatewayUrl = new Uri(new Uri(_fixture.ServerOptions.PublicBaseUrl), "/hub/gateway");
        return new HubConnectionBuilder()
            .WithUrl(gatewayUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();
    }

    [Fact]
    public async Task SessionChatHub_Should_Stream_Chat_Completion_Successfully()
    {
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        var activeConfig = modelManager.GetActiveSettings();

        if (activeConfig == null)
        {
            await using var adminConn = CreateGatewayConnection(_fixture.ServerOptions.AdminApiKey);
            await adminConn.StartAsync();
            await adminConn.InvokeAsync("LoadModelAsync", ModelOptions.RepoId, "Default");
        }

        await using var hubConnection = CreateGatewayConnection(_fixture.ServerOptions.TenantApiKey);
        string sessionId = $"test-session-{Guid.NewGuid():N}";
        var completionBuilder = new StringBuilder();
        var tcs = new TaskCompletionSource<string>();

        hubConnection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            if (delta.SessionId == sessionId)
            {
                completionBuilder.Append(delta.Content);
                if (delta.IsDone)
                {
                    tcs.TrySetResult(completionBuilder.ToString());
                }
            }
        });

        hubConnection.On<string>("ReceiveError", err =>
        {
            tcs.TrySetException(new InvalidOperationException($"Hub error: {err}"));
        });

        await hubConnection.StartAsync();
        await hubConnection.InvokeAsync("JoinSession", sessionId, ModelOptions.RepoId);

        var message = new ChatMessage("user", "Hello! Return one word: Pong.");
        await hubConnection.InvokeAsync("SendPromptDelta", sessionId, message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(30000));
        result.Should().Be(tcs.Task, "The model must respond within timeout via GatewayHub");

        var text = await tcs.Task;
        text.Should().NotBeNullOrWhiteSpace();

        await hubConnection.InvokeAsync("LeaveSession", sessionId);
        await hubConnection.StopAsync();
    }
}