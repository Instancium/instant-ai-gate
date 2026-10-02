namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
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
    }

    [Fact]
    public async Task SessionChatHub_Should_Stream_Chat_Completion_Successfully()
    {
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        var activeConfig = modelManager.GetActiveSettings();

        if (activeConfig == null)
        {
            var adminClient = _fixture.CreateAdminClient();
            adminClient.DefaultRequestHeaders.Add("X-API-Key", _fixture.ServerOptions.AdminApiKey);
            var response = await adminClient.PostAsJsonAsync("admin/models/load", new { RepoId = ModelOptions.RepoId, Profile = "Default" });
            response.EnsureSuccessStatusCode();
        }

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_fixture.CreatePublicClient().BaseAddress!, "hub/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
            })
            .Build();

        await hubConnection.StartAsync();

        string sessionId = $"test-session-{Guid.NewGuid():N}";
        await hubConnection.InvokeAsync("CreateSession", new SessionStartRequest(sessionId, ModelOptions.RepoId));

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

        var message = new ChatMessage("user", "Hello! Return one word: Pong.");
        await hubConnection.InvokeAsync("SendPromptDelta", new SessionPromptDelta(sessionId, message));

        var result = await Task.WhenAny(tcs.Task, Task.Delay(30000));
        result.Should().Be(tcs.Task, "The model must respond within timeout via SignalR");

        var text = await tcs.Task;
        text.Should().NotBeNullOrWhiteSpace();

        await hubConnection.InvokeAsync("CloseSession", sessionId);
        await hubConnection.StopAsync();
    }
}