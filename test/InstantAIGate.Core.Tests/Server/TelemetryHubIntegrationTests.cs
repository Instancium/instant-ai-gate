using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InstantAIGate.Core.Tests.Server;

public class TelemetryHubIntegrationTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private readonly string _adminToken = "test-admin-secret"; // From GatewayTestFixture

    public TelemetryHubIntegrationTests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TelemetryHub_ReceivesMetrics_DuringInference()
    {
        // 1. Setup SignalR Client pointing to Admin Port (5001)
        var hubUrl = new Uri("http://localhost:5001/hub/telemetry");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(_adminToken)!;
            })
            .Build();

        var metricsReceived = new TaskCompletionSource<bool>();
        var logsReceived = new List<string>();

        connection.On<object, object>("ReceiveMetrics", (metrics, native) =>
        {
            metricsReceived.TrySetResult(true);
        });

        connection.On<string, string, string>("ReceiveLog", (level, category, message) =>
        {
            logsReceived.Add(message);
        });

        await connection.StartAsync();

        // 2. Trigger an action to generate logs (e.g., attempt to load a model)
        var adminClient = _fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost:5001") });
        var loadRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/admin/models/load");
        loadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        loadRequest.Content = JsonContent.Create(new { RepoId = "qwen3-vl-8b-instruct" });

        await adminClient.SendAsync(loadRequest);

        // 3. Assertions
        var metricsArrived = await Task.WhenAny(metricsReceived.Task, Task.Delay(2000)) == metricsReceived.Task;

        Assert.True(metricsArrived, "Failed to receive metrics broadcast within 2 seconds.");
        Assert.Contains(logsReceived, msg => msg.Contains("qwen3-vl-8b-instruct")); // Or native llama.cpp load logs

        await connection.StopAsync();
    }
}