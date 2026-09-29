using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;

namespace InstantAIGate.Core.Tests.Server;

public class GatewayTestFixture : WebApplicationFactory<Program>
{
    /// <summary>Addresses/secrets of the test stand loaded from the test project appsettings.json.</summary>
    public TestServerOptions ServerOptions { get; } = TestConfig.Server;

    /// <summary>Model options of the test stand loaded from the test project appsettings.json.</summary>
    public TestModelOptions ModelOptions { get; } = TestConfig.Model;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // All values come from the test configuration file (appsettings.json);
            // typed options already contain safe defaults in case a section is missing.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "InstantAIGate:AdminApiKey", ServerOptions.AdminApiKey },
                { "Kestrel:Endpoints:PublicEndpoint:Url", ServerOptions.PublicEndpointUrl },
                { "Kestrel:Endpoints:AdminEndpoint:Url", ServerOptions.AdminEndpointUrl },
                // Disable background startup autoload during tests
                { "InstantAIGate:StartupModel:Enabled", "false" }
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestServerPortEmulatorFilter>();

            // Mock model downloader to avoid real external HTTP traffic during integration tests
            services.AddSingleton<IModelDownloader, TestModelDownloaderStub>();
        });
    }

    /// <summary>Helper: test client emulating requests to the public API (port from config).</summary>
    public HttpClient CreatePublicClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ServerOptions.PublicBaseUrl)
        });

    /// <summary>Helper: test client emulating requests to the admin API (port from config).</summary>
    public HttpClient CreateAdminClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ServerOptions.AdminBaseUrl)
        });

    /// <summary>Full SignalR telemetry hub URL (admin port + path from config).</summary>
    public Uri TelemetryHubUrl => ServerOptions.TelemetryHubUrl;

    private sealed class TestModelDownloaderStub : IModelDownloader
    {
        public async Task DownloadModelAsync(
            string modelId,
            IReadOnlyList<string> downloadUrls,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default)
        {
            // Emulate instant progress broadcasts
            progress.Report(new DownloadProgress(modelId, 50_000_000, 100_000_000, 25_000_000, 50.0f));
            await Task.Delay(50, ct);
            progress.Report(new DownloadProgress(modelId, 100_000_000, 100_000_000, 25_000_000, 100.0f));
        }

        public Task CancelDownloadAsync(string modelId) => Task.CompletedTask;
    }
}

public class TestServerPortEmulatorFilter : IStartupFilter
{
    public System.Action<IApplicationBuilder> Configure(System.Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextDelegate) =>
            {
                // Emulate LocalPort based on the request host for TestServer
                if (context.Request.Host.Port.HasValue)
                {
                    context.Connection.LocalPort = context.Request.Host.Port.Value;
                }
                await nextDelegate();
            });
            next(app);
        };
    }
}
