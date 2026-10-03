namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

public class TelemetryHubIntegrationTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;
    private readonly TestServerOptions _serverOptions;
    private readonly string _adminToken;
    private readonly string _testRepoId;

    public TelemetryHubIntegrationTests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
        _serverOptions = fixture.ServerOptions;
        _adminToken = _serverOptions.AdminApiKey;
        _testRepoId = fixture.ModelOptions.RepoId;
        NativeLibraryLoader.Load();
    }

    private HubConnection CreateGatewayAdminConnection()
    {
        var gatewayUrl = new Uri(new Uri(_serverOptions.PublicBaseUrl), "/hub/gateway");
        return new HubConnectionBuilder()
            .WithUrl(gatewayUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(_adminToken)!;
            })
            .Build();
    }

    [Fact]
    public async Task TelemetryHub_ReceivesSsrProgress_DuringModelDownload()
    {
        await using var connection = CreateGatewayAdminConnection();
        var progressReceived = new TaskCompletionSource<DownloadProgress>();

        connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
        {
            if (progress.ModelId == _testRepoId)
            {
                progressReceived.TrySetResult(progress);
            }
        });

        await connection.StartAsync();

        await connection.InvokeAsync("DownloadModelAsync", _testRepoId);

        var progressArrived = await Task.WhenAny(progressReceived.Task, Task.Delay(TimeSpan.FromSeconds(5))) == progressReceived.Task;
        progressArrived.Should().BeTrue("SSR download progress must be broadcast to admin group over GatewayHub within 5 seconds.");

        var progressData = await progressReceived.Task;
        progressData.Should().NotBeNull();
        progressData.ModelId.Should().Be(_testRepoId);
        progressData.TotalBytes.Should().BeGreaterThanOrEqualTo(0);

        await connection.StopAsync();
    }

    [Fact]
    public async Task TelemetryHub_ReceivesMetrics_DuringInference()
    {
        await using var connection = CreateGatewayAdminConnection();
        var metricsReceived = new TaskCompletionSource<bool>();
        var logsReceived = new List<string>();

        connection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, native) =>
        {
            metricsReceived.TrySetResult(true);
        });

        connection.On<string, string, string>("ReceiveLog", (level, category, message) =>
        {
            logsReceived.Add(message);
        });

        await connection.StartAsync();

        await connection.InvokeAsync("LoadModelAsync", _testRepoId, "Default");

        var metricsArrived = await Task.WhenAny(metricsReceived.Task, Task.Delay(TimeSpan.FromSeconds(3))) == metricsReceived.Task;
        metricsArrived.Should().BeTrue("Metrics broadcast must be received by admin connection on GatewayHub within 3 seconds.");

        await connection.StopAsync();
    }
}