using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InstantAIGate.Core.Tests.Server;

public class TelemetryHubIntegrationTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private readonly TestServerOptions _serverOptions;

    // Admin token and hub URL come from the test project appsettings.json.
    private readonly string _adminToken;

    // Target model identifier comes from the test project appsettings.json.
    private readonly string _testRepoId;

    public TelemetryHubIntegrationTests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
        _serverOptions = fixture.ServerOptions;
        _adminToken = _serverOptions.AdminApiKey;
        _testRepoId = fixture.ModelOptions.RepoId;
    }

    [Fact]
    public async Task TelemetryHub_ReceivesSsrProgress_DuringModelDownload()
    {
        var hubUrl = _fixture.TelemetryHubUrl;
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(_adminToken)!;
            })
            .Build();

        var progressReceived = new TaskCompletionSource<DownloadProgress>();

        connection.On<DownloadProgress>("ReceiveSsrProgress", progress =>
        {
            progressReceived.TrySetResult(progress);
        });

        await connection.StartAsync();

        var adminClient = _fixture.CreateAdminClient();

        var downloadRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/models/download");
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        downloadRequest.Content = JsonContent.Create(new { RepoId = _testRepoId });

        var response = await adminClient.SendAsync(downloadRequest);
        response.EnsureSuccessStatusCode();

        var progressArrived = await Task.WhenAny(progressReceived.Task, Task.Delay(TimeSpan.FromSeconds(5))) == progressReceived.Task;

        Assert.True(progressArrived, "Failed to receive SSR download progress broadcast within 5 seconds.");

        var progressData = await progressReceived.Task;
        Assert.NotNull(progressData);
        Assert.Equal(_testRepoId, progressData.ModelId);
        Assert.True(progressData.TotalBytes >= 0, "Total bytes should be initialized.");

        await connection.StopAsync();
    }

    [Fact]
    public async Task TelemetryHub_ReceivesMetrics_DuringInference()
    {
        // 1. Setup SignalR Client pointing to the admin port (from test configuration)
        var hubUrl = _fixture.TelemetryHubUrl;

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
        var adminClient = _fixture.CreateAdminClient();
        var loadRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/models/load");
        loadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        loadRequest.Content = JsonContent.Create(new { RepoId = _testRepoId });

        await adminClient.SendAsync(loadRequest);

        // 3. Assertions
        var metricsArrived = await Task.WhenAny(metricsReceived.Task, Task.Delay(2000)) == metricsReceived.Task;

        Assert.True(metricsArrived, "Failed to receive metrics broadcast within 2 seconds.");
        Assert.Contains(logsReceived, msg => msg.Contains(_testRepoId)); // Or native llama.cpp load logs

        await connection.StopAsync();
    }
}
