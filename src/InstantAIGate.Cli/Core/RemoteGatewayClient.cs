namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public class RemoteGatewayClient : IGatewayClient, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _adminHubUrl;
    private readonly string _adminKey;
    private HubConnection? _telemetryConnection;
    private HubConnection? _chatHubConnection;

    public event Action<int>? QueuePositionReceived;
    public event Action<string, string, string>? LogReceived;

    public RemoteGatewayClient(HttpClient httpClient, string publicUrl, string adminHubUrl, string adminKey)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(publicUrl);
        }
        _adminHubUrl = adminHubUrl;
        _adminKey = adminKey;
    }

    private async Task EnsureChatHubConnectedAsync(CancellationToken ct)
    {
        if (_chatHubConnection != null && _chatHubConnection.State == HubConnectionState.Connected)
        {
            return;
        }

        string chatHubUrl = new Uri(_httpClient.BaseAddress!, "/hub/chat").ToString();
        _chatHubConnection = new HubConnectionBuilder()
            .WithUrl(chatHubUrl, options =>
            {
                if (!string.IsNullOrWhiteSpace(_adminKey))
                {
                    options.AccessTokenProvider = () => Task.FromResult(_adminKey)!;
                }
            })
            .WithAutomaticReconnect()
            .Build();

        await _chatHubConnection.StartAsync(ct);
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_chatHubConnection != null && _chatHubConnection.State == HubConnectionState.Connected)
        {
            await _chatHubConnection.InvokeAsync("LeaveSession", sessionId, ct);
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId, string repoId, ChatMessage deltaMessage, [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureChatHubConnectedAsync(ct);
        await _chatHubConnection!.InvokeAsync("JoinSession", sessionId, repoId, ct);

        var channel = Channel.CreateUnbounded<string>();
        var tcs = new TaskCompletionSource();

        using var tokenSub = _chatHubConnection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            if (delta.IsDone)
            {
                tcs.TrySetResult();
                channel.Writer.TryComplete();
            }
            else
            {
                channel.Writer.TryWrite(delta.Content);
            }
        });

        using var errSub = _chatHubConnection.On<string>("ReceiveError", error =>
        {
            var ex = new InvalidOperationException($"Remote Inference Error: {error}");
            tcs.TrySetException(ex);
            channel.Writer.TryComplete(ex);
        });

        using var overflowSub = _chatHubConnection.On<ContextOverflowException>("ReceiveContextOverflow", overflow =>
        {
            tcs.TrySetException(overflow);
            channel.Writer.TryComplete(overflow);
        });

        await _chatHubConnection.InvokeAsync("SendPromptDelta", sessionId, deltaMessage, ct);

        await foreach (var token in channel.Reader.ReadAllAsync(ct))
        {
            yield return token;
        }

        await tcs.Task;
    }

    public Task LoadModelAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UnloadModelAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task SwapModelAsync(string repoId, string? profile = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task DownloadModelAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task SubscribeToModelDownloadAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        if (!string.IsNullOrWhiteSpace(_adminKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminKey);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<ActiveModelDetailsResponse>(cancellationToken: ct);
            if (content?.ActiveDetails != null)
            {
                return content.ActiveDetails;
            }
        }

        return new NativeModelDetails
        {
            ContextSize = 4096,
            Backend = "remote",
            GpuLayers = 99
        };
    }

    public async Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureChatHubConnectedAsync(ct);
        return await _chatHubConnection!.InvokeAsync<int>("GetSessionTokenCount", sessionId, ct);
    }

    public async Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default)
    {
        await EnsureChatHubConnectedAsync(ct);
        await _chatHubConnection!.InvokeAsync("RollbackSession", sessionId, targetPosition, ct);
    }

    public async Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default)
    {
        await EnsureChatHubConnectedAsync(ct);
        await _chatHubConnection!.InvokeAsync("ShiftSessionCache", sessionId, startPos, count, ct);
    }

    public async Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct)
    {
        _telemetryConnection = new HubConnectionBuilder()
            .WithUrl(_adminHubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_adminKey)!;
            })
            .WithAutomaticReconnect()
            .Build();

        _telemetryConnection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, _) => onMetrics(metrics));
        _telemetryConnection.On<DownloadProgress>("ReceiveSsrProgress", progress => onSsrProgress(progress));

        await _telemetryConnection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_telemetryConnection != null)
        {
            await _telemetryConnection.DisposeAsync();
        }

        if (_chatHubConnection != null)
        {
            await _chatHubConnection.DisposeAsync();
        }
    }

    private sealed record ActiveModelDetailsResponse(NativeModelDetails? ActiveDetails);
}