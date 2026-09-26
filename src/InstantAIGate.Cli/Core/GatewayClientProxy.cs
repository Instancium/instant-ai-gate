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

    public void SwitchToRemote(string url, string key)
    {
        var httpClient = _httpClientFactory.CreateClient();
        _activeClient = new RemoteGatewayClient(httpClient, url, key);
    }

    public void SwitchToLocal()
    {
        _activeClient = _serviceProvider.GetRequiredService<LocalGatewayClient>();
    }

    public IAsyncEnumerable<string> StreamChatAsync(string repoId, IEnumerable<ChatMessage> messages, CancellationToken ct)
        => _activeClient.StreamChatAsync(repoId, messages, ct);

    public Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct)
        => _activeClient.ConnectTelemetryAsync(onMetrics, onSsrProgress, ct);

    public Task LoadModelAsync(string repoId, CancellationToken ct = default)
        => _activeClient.LoadModelAsync(repoId, ct);
}