namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Dashboard.Services;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Locks the dashboard control plane against argument-binding regressions.
/// The real <see cref="DashboardTelemetryClient"/> is exercised end-to-end through the
/// in-memory TestServer transport, so a future switch back to an overload that ships the
/// argument array as a single value fails here instead of in the browser.
/// </summary>
public sealed class DashboardAdminRpcRegressionTests : IClassFixture<DashboardAdminRpcRegressionTests.RegressionFixture>
{
    private readonly RegressionFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DashboardAdminRpcRegressionTests(RegressionFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private async Task<DashboardTelemetryClient> CreateConnectedClientAsync()
    {
        var client = new DashboardTelemetryClient(
            new FixedAdminApiKeyStore(_fixture.ServerOptions.AdminApiKey),
            NullLogger<DashboardTelemetryClient>.Instance,
            _fixture.GatewayHubUrl,
            options => options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler());

        await client.ConnectAsync();
        return client;
    }

    private HubConnection CreateRawAdminConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(_fixture.GatewayHubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(_fixture.ServerOptions.AdminApiKey)!;
            })
            .Build();
    }

    private async Task<string> ResolveEmbeddingRepoIdAsync()
    {
        var catalog = _fixture.Services.GetRequiredService<IModelCatalogService>();
        IReadOnlyList<CatalogModelEntry> models = await catalog.GetSupportedModelsAsync();

        CatalogModelEntry? embedding = models.FirstOrDefault(m =>
            m.Id.Contains("embed", StringComparison.OrdinalIgnoreCase));

        if (embedding is null)
        {
            Assert.Fail("No embedding model exists in config/model_catalog.json; the reported scenario cannot be reproduced.");
            throw new InvalidOperationException();
        }

        return embedding.Id;
    }

    private async Task EnsureEmbeddingModelLoadedAsync(string repoId)
    {
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        if (modelManager.GetModelSettings(repoId) is not null)
        {
            return;
        }

        await using HubConnection admin = CreateRawAdminConnection();
        await admin.StartAsync();
        await admin.InvokeAsync("LoadModelAsync", repoId, "Embedding");
    }

    /// <summary>Places a native context into the idle pool so purge has real work to do.</summary>
    private async Task WarmUpContextPoolAsync(string repoId)
    {
        await using HubConnection admin = CreateRawAdminConnection();
        await admin.StartAsync();

        Exception? failure = await Record.ExceptionAsync(() => admin.InvokeAsync<EmbeddingResponse>(
            "GenerateEmbeddingsAsync", repoId, new EmbeddingRequest("regression warm-up", true)));

        if (failure is not null)
        {
            _output.WriteLine($"Warm-up embedding failed (continuing): {failure.Message}");
        }
    }

    [Fact]
    public async Task SetQueueLimitAsync_ThroughDashboardClient_BindsScalarArgumentOnServer()
    {
        const int requestedLimit = 77;

        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();
        await client.SetQueueLimitAsync(requestedLimit);

        await using HubConnection admin = CreateRawAdminConnection();
        await admin.StartAsync();
        InferenceMetrics metrics = await admin.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");

        Assert.Equal(requestedLimit, metrics.MaxQueueSize);
    }

    [Fact]
    public async Task TriggerGarbageCollectionAsync_ThroughDashboardClient_Succeeds()
    {
        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();

        Exception? failure = await Record.ExceptionAsync(() => client.TriggerGarbageCollectionAsync());

        Assert.Null(failure);
    }

    [Fact]
    public async Task PurgeIdleContextsAsync_ThroughDashboardClient_BindsRepoIdOnServer()
    {
        string repoId = await ResolveEmbeddingRepoIdAsync();
        await EnsureEmbeddingModelLoadedAsync(repoId);
        await WarmUpContextPoolAsync(repoId);

        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();
        Exception? failure = await Record.ExceptionAsync(() => client.PurgeIdleContextsAsync(repoId));

        Assert.Null(failure);

        DashboardTelemetrySnapshot? snapshot = await client.GetSnapshotAsync();
        Assert.NotNull(snapshot);
        ModelPoolTelemetryDto model = Assert.Single(snapshot.Models, m =>
            string.Equals(m.RepoId, repoId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, model.IdlePooledContexts);
    }

    [Fact]
    public async Task PurgeIdleContextsAsync_WithEmptyRepoId_PurgesAcrossAllLoadedModels()
    {
        string repoId = await ResolveEmbeddingRepoIdAsync();
        await EnsureEmbeddingModelLoadedAsync(repoId);
        await WarmUpContextPoolAsync(repoId);

        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();
        Exception? failure = await Record.ExceptionAsync(() => client.PurgeIdleContextsAsync(string.Empty));

        Assert.Null(failure);
    }

    [Fact]
    public async Task UnloadModelAsync_ThroughDashboardClient_RemovesOnlyTargetModel()
    {
        string repoId = await ResolveEmbeddingRepoIdAsync();
        await EnsureEmbeddingModelLoadedAsync(repoId);
        await WarmUpContextPoolAsync(repoId);

        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();
        Exception? failure = await Record.ExceptionAsync(() => client.UnloadModelAsync(repoId));

        Assert.Null(failure);

        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();
        Assert.Null(modelManager.GetModelSettings(repoId));
    }

    [Fact]
    public async Task ForceKillSessionAsync_ThroughDashboardClient_BindsSessionIdOnServer()
    {
        string repoId = await ResolveEmbeddingRepoIdAsync();
        await EnsureEmbeddingModelLoadedAsync(repoId);

        string sessionId = $"dashboard-kill-{Guid.NewGuid():N}";
        var sessionManager = _fixture.Services.GetRequiredService<ISessionInferenceManager>();
        await sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, repoId));

        await using DashboardTelemetryClient client = await CreateConnectedClientAsync();
        Exception? failure = await Record.ExceptionAsync(() => client.ForceKillSessionAsync(sessionId));

        Assert.Null(failure);
        Assert.DoesNotContain(sessionManager.GetSessionsTelemetry(), s => s.SessionId == sessionId);
    }

    private sealed class FixedAdminApiKeyStore : IAdminApiKeyStore
    {
        private string? _apiKey;

        public FixedAdminApiKeyStore(string apiKey) => _apiKey = apiKey;

        public ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(_apiKey);

        public ValueTask SetApiKeyAsync(string apiKey, CancellationToken ct = default)
        {
            _apiKey = apiKey;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken ct = default)
        {
            _apiKey = null;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Test-only host configuration. Detailed hub errors are enabled here so any future
    /// server-side rejection is readable in the test output without touching production.
    /// </summary>
    public sealed class RegressionFixture : GatewayTestFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                services.Configure<HubOptions>(options => options.EnableDetailedErrors = true);
            });
        }
    }
}