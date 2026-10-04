namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Server;
using Microsoft.AspNetCore.Hosting;
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
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Worker_Should_Automatically_Load_Model_On_Startup()
    {
        var client = _factory.CreateClient();
        var stateManager = _factory.Services.GetRequiredService<IGatewayStateManager>();
        var modelManager = _factory.Services.GetRequiredService<IModelManager>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!cts.Token.IsCancellationRequested)
        {
            var snapshot = stateManager.GetSnapshot();
            if (snapshot.Status == GatewayOperationalStatus.Ready)
            {
                break;
            }

            if (snapshot.Status == GatewayOperationalStatus.Faulted)
            {
                Assert.Fail($"Startup worker entered Faulted state: {snapshot.ErrorMessage}");
            }

            await Task.Delay(50, cts.Token);
        }

        var activeConfig = modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        Assert.Equal(ModelOptions.RepoId, activeConfig.RepoId);
    }
}