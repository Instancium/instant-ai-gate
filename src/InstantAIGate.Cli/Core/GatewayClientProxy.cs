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

    public event Action<int>? QueuePositionReceived;
    public event Action<string, string, string>? LogReceived;
    public event Action<DownloadProgress>? DownloadProgressReceived;
    public event Action<GatewayStatusDetails>? GatewayStatusReceived;

    public GatewayClientProxy(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IGatewayClient initialClient)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _activeClient = initialClient;
        BindClientEvents(_activeClient);
    }

    private void BindClientEvents(IGatewayClient client)
    {
        client.QueuePositionReceived += pos => QueuePositionReceived?.Invoke(pos);
        client.LogReceived += (lvl, cat, msg) => LogReceived?.Invoke(lvl, cat, msg);
        client.DownloadProgressReceived += p => DownloadProgressReceived?.Invoke(p);
        client.GatewayStatusReceived += s => GatewayStatusReceived?.Invoke(s);
    }

    public async Task SwitchToRemoteAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var probeClient = _httpClientFactory.CreateClient();
        probeClient.Timeout = TimeSpan.FromSeconds(3);
        string healthEndpoint = $"{baseUrl.TrimEnd('/')}/health/live";
        using var response = await probeClient.GetAsync(healthEndpoint, ct);
        response.EnsureSuccessStatusCode();

        var remoteHttpClient = _httpClientFactory.CreateClient();
        string hubUrl = $"{baseUrl.TrimEnd('/')}/hub/gateway";
        var remoteClient = new RemoteGatewayClient(remoteHttpClient, baseUrl, hubUrl, apiKey);
        _activeClient = remoteClient;
        BindClientEvents(_activeClient);
    }

    public Task SwitchToRemoteAsync(string publicUrl, string adminHubUrl, string adminKey, CancellationToken ct = default) =>
        SwitchToRemoteAsync(publicUrl, adminKey, ct);

    public void SwitchToLocal()
    {
        _activeClient = _serviceProvider.GetRequiredService<LocalGatewayClient>();
        BindClientEvents(_activeClient);
    }

    public Task EndSessionAsync(string sessionId, CancellationToken ct = default) =>
        _activeClient.EndSessionAsync(sessionId, ct);

    public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, CancellationToken ct) =>
        _activeClient.StreamChatAsync(sessionId, repoId, deltaMessage, ct);

    public Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct) =>
        _activeClient.ConnectTelemetryAsync(onMetrics, onSsrProgress, ct);

    public Task LoadModelAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.LoadModelAsync(repoId, ct);

    public Task UnloadModelAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.UnloadModelAsync(repoId, ct);

    public Task SwapModelAsync(string repoId, string? profile = null, CancellationToken ct = default) =>
        _activeClient.SwapModelAsync(repoId, profile, ct);

    public Task DownloadModelAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.DownloadModelAsync(repoId, ct);

    public Task SubscribeToModelDownloadAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.SubscribeToModelDownloadAsync(repoId, ct);

    public Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken ct = default) =>
        _activeClient.UnsubscribeFromModelDownloadAsync(repoId, ct);

    public Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default) =>
        _activeClient.GetActiveModelDetailsAsync(ct);

    public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken ct = default) =>
        _activeClient.GetGatewayStatusAsync(ct);

    public Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default) =>
        _activeClient.GetSessionTokenCountAsync(sessionId, ct);

    public Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default) =>
        _activeClient.RollbackSessionAsync(sessionId, targetPosition, ct);

    public Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default) =>
        _activeClient.ShiftSessionMemoryAsync(sessionId, startPos, count, ct);
}