namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class GatewayClientProxy : IGatewayClient
{
    private IGatewayClient _activeClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    public GatewayClientProxy(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IGatewayClient initialClient)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _activeClient = initialClient;
    }

    public async Task SwitchToRemoteAsync(string publicUrl, string adminHubUrl, string adminKey, CancellationToken ct = default)
    {
        using var probeClient = _httpClientFactory.CreateClient();
        probeClient.Timeout = TimeSpan.FromSeconds(3);

        string healthEndpoint = $"{publicUrl.TrimEnd('/')}/health/live";
        using var response = await probeClient.GetAsync(healthEndpoint, ct);
        response.EnsureSuccessStatusCode();

        var remoteClient = _httpClientFactory.CreateClient();
        _activeClient = new RemoteGatewayClient(remoteClient, publicUrl, adminHubUrl, adminKey);
    }


    public Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        // Forward the release command to whichever client (Local or Remote) is currently active
        return _activeClient.EndSessionAsync(sessionId, ct);
    }

    public void SwitchToLocal()
    {
        _activeClient = _serviceProvider.GetRequiredService<LocalGatewayClient>();
    }

    public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, CancellationToken ct)
        => _activeClient.StreamChatAsync(sessionId, repoId, deltaMessage, ct);

    public Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct)
        => _activeClient.ConnectTelemetryAsync(onMetrics, onSsrProgress, ct);

    public Task LoadModelAsync(string repoId, CancellationToken ct = default)
        => _activeClient.LoadModelAsync(repoId, ct);
}