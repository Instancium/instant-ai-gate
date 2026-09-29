namespace InstantAIGate.Core.Tests.Server;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InstantAIGate.Core.Interfaces.Inference;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;

public class ModelStartupWorkerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ModelStartupWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "InstantAIGate:StartupModel:Enabled", "true" },
                    { "InstantAIGate:StartupModel:RepoId", "qwen3-vl-8b-instruct" },
                    { "InstantAIGate:StartupModel:Profile", "Default" }
                });
            });
        });
    }

    [Fact]
    public async Task Worker_Should_Automatically_Load_Model_On_Startup()
    {
        // Act: The host starts and triggers IHostedService automatically
        var client = _factory.CreateClient();
        var modelManager = _factory.Services.GetRequiredService<IModelManager>();

        // Assert: Wait for the background worker to acquire the model
        // In a real scenario, implement a retry policy or Task.Delay to wait for load completion
        var activeConfig = modelManager.GetActiveSettings();

        Assert.NotNull(activeConfig);
        Assert.Equal("qwen3-vl-8b-instruct", activeConfig.RepoId);
    }
}