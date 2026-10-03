namespace InstantAIGate.Core.Tests.Server;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;

public class GatewayTestFixture : WebApplicationFactory<Program>
{
    public TestServerOptions ServerOptions { get; } = TestConfig.Server;
    public TestModelOptions ModelOptions { get; } = TestConfig.Model;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "InstantAIGate:AdminApiKey", ServerOptions.AdminApiKey },
                { "InstantAIGate:StartupModel:Enabled", "false" }
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IModelDownloader, TestModelDownloaderStub>();
        });
    }

    public HttpClient CreatePublicClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(ServerOptions.PublicBaseUrl)
    });

    public HttpClient CreateAdminClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(ServerOptions.PublicBaseUrl)
    });

    public Uri TelemetryHubUrl => new(new Uri(ServerOptions.PublicBaseUrl), "/hub/gateway");

    private sealed class TestModelDownloaderStub : IModelDownloader
    {
        public async Task DownloadModelAsync(
            string modelId,
            IReadOnlyList<string> downloadUrls,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default)
        {
            progress.Report(new DownloadProgress(modelId, 50_000_000, 100_000_000, 25_000_000, 50.0f));
            await Task.Delay(50, ct);
            progress.Report(new DownloadProgress(modelId, 100_000_000, 100_000_000, 25_000_000, 100.0f));
        }

        public Task CancelDownloadAsync(string modelId) => Task.CompletedTask;
    }
}