// src\InstantAIGate.Cli\Core\RemoteGatewayClient.cs
namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
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

    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_chatHubConnection != null && _chatHubConnection.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
        {
            await _chatHubConnection.InvokeAsync("LeaveSession", sessionId, ct);
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_chatHubConnection == null || _chatHubConnection.State == HubConnectionState.Disconnected)
        {
            string chatHubUrl = new Uri(_httpClient.BaseAddress!, "/hub/chat").ToString();
            _chatHubConnection = new HubConnectionBuilder()
                .WithUrl(chatHubUrl, options =>
                {
                    if (!string.IsNullOrWhiteSpace(_adminKey))
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_adminKey)!;
                    }
                })
                .Build();

            await _chatHubConnection.StartAsync(ct);
            await _chatHubConnection.InvokeAsync("JoinSession", sessionId, repoId, ct);
        }

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


        await _chatHubConnection.InvokeAsync("SendPromptDelta", sessionId, deltaMessage, ct);

        await foreach (var token in channel.Reader.ReadAllAsync(ct))
        {
            yield return token;
        }

        await tcs.Task;
    }

    public Task LoadModelAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public async Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct)
    {
        _telemetryConnection = new HubConnectionBuilder()
            .WithUrl(_adminHubUrl, options => { options.AccessTokenProvider = () => Task.FromResult(_adminKey)!; })
            .WithAutomaticReconnect()
            .Build();

        _telemetryConnection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, _) => onMetrics(metrics));
        _telemetryConnection.On<DownloadProgress>("ReceiveSsrProgress", progress => onSsrProgress(progress));
        await _telemetryConnection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_telemetryConnection != null) await _telemetryConnection.DisposeAsync();
        if (_chatHubConnection != null) await _chatHubConnection.DisposeAsync();
    }
}