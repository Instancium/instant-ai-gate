namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
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
        return _activeClient.EndSessionAsync(sessionId, ct);
    }

    public void SwitchToLocal()
    {
        _activeClient = _serviceProvider.GetRequiredService<LocalGatewayClient>();
    }

    public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, CancellationToken ct) =>
        _activeClient.StreamChatAsync(sessionId, repoId, deltaMessage, ct);

    public Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct) =>
        _activeClient.ConnectTelemetryAsync(onMetrics, onSsrProgress, ct);

    public Task LoadModelAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.LoadModelAsync(repoId, ct);

    public Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default) =>
        _activeClient.GetActiveModelDetailsAsync(ct);

    public Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default) =>
        _activeClient.GetSessionTokenCountAsync(sessionId, ct);

    public Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default) =>
        _activeClient.RollbackSessionAsync(sessionId, targetPosition, ct);

    public Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default) =>
        _activeClient.ShiftSessionMemoryAsync(sessionId, startPos, count, ct);
}