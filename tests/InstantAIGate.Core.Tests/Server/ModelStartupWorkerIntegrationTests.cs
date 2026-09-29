namespace InstantAIGate.Core.Tests.Server;

using Microsoft.AspNetCore.Mvc.Testing;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class ModelStartupWorkerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    // Target test model identifier comes from the test project appsettings.json.
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
    public async Task Worker_Should_Automatically_Load_Model_On_Startup()
    {
        // Act: The host starts and triggers IHostedService automatically
        var client = _factory.CreateClient();
        var modelManager = _factory.Services.GetRequiredService<IModelManager>();

        // Assert: Wait for the background worker to acquire the model
        // In a real scenario, implement a retry policy or Task.Delay to wait for load completion
        var activeConfig = modelManager.GetActiveSettings();

        Assert.NotNull(activeConfig);
        Assert.Equal(ModelOptions.RepoId, activeConfig.RepoId);
    }
}