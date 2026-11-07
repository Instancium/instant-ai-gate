namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class ModelStartupWorkerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly TestModelOptions ModelOptions = TestConfig.Model;

    public ModelStartupWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "InstantAIGate:StartupModel:Enabled", "true" },
                    { "InstantAIGate:StartupModel:RepoId", ModelOptions.RepoId },
                    { "InstantAIGate:StartupModel:Profile", "Default" }
                });
            });
        });
    }

    [Fact]
    public async Task Host_Should_Be_Immediately_Responsive_On_Liveness_Probe()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Worker_Should_Automatically_Load_Model_On_Startup()
    {
        var client = _factory.CreateClient();
        var stateManager = _factory.Services.GetRequiredService<IGatewayStateManager>();
        var modelManager = _factory.Services.GetRequiredService<IModelManager>();

        // Increase timeout for full test suite runs (I/O & CPU contention)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool isReady = false;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var snapshot = stateManager.GetSnapshot();

                if (snapshot.Status == GatewayOperationalStatus.Ready)
                {
                    isReady = true;
                    break;
                }

                if (snapshot.Status == GatewayOperationalStatus.Faulted)
                {
                    Assert.Fail($"Startup worker entered faulted state: {snapshot.ErrorMessage}");
                }

                await Task.Delay(50, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            // Swallow the raw cancellation exception to handle it gracefully below
        }

        Assert.True(isReady, "Timeout: ModelStartupWorker did not reach 'Ready' state within 30 seconds under load.");

        var activeConfig = modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        Assert.Equal(ModelOptions.RepoId, activeConfig.RepoId);
    }
}