namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public sealed class RemoteGatewayClient : IGatewayClient, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _hubUrl;
    private readonly string _apiKey;

    private HubConnection? _hubConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    // Callbacks for global observability
    private Action<InferenceMetrics>? _onMetricsCallback;
    private Action<DownloadProgress>? _onDownloadProgressCallback;

    // Per-session channels for multiplexed streaming
    private readonly ConcurrentDictionary<string, Channel<string>> _activeChannels = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _activeCompletions = new();

    public event Action<int>? QueuePositionReceived;
    public event Action<string, string, string>? LogReceived;

    public RemoteGatewayClient(HttpClient httpClient, string baseUrl, string hubUrl, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _hubUrl = string.IsNullOrWhiteSpace(hubUrl) ? $"{_baseUrl}/hub/gateway" : hubUrl;
        _apiKey = apiKey ?? string.Empty;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_baseUrl);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            return;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                return;
            }

            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    if (!string.IsNullOrWhiteSpace(_apiKey))
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_apiKey)!;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubEventHandlers(_hubConnection);
            await _hubConnection.StartAsync(ct);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void RegisterHubEventHandlers(HubConnection connection)
    {
        connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            if (_activeChannels.TryGetValue(delta.SessionId, out var channel))
            {
                if (delta.IsDone)
                {
                    channel.Writer.TryComplete();
                    if (_activeCompletions.TryGetValue(delta.SessionId, out var tcs))
                    {
                        tcs.TrySetResult();
                    }
                }
                else if (!string.IsNullOrEmpty(delta.Content))
                {
                    channel.Writer.TryWrite(delta.Content);
                }
            }
        });

        connection.On<string>("ReceiveError", error =>
        {
            var exception = new InvalidOperationException($"Remote Gateway Error: {error}");
            foreach (var kvp in _activeChannels)
            {
                kvp.Value.Writer.TryComplete(exception);
            }
            foreach (var kvp in _activeCompletions)
            {
                kvp.Value.TrySetException(exception);
            }
        });

        connection.On<ContextOverflowException>("ReceiveContextOverflow", overflow =>
        {
            if (_activeChannels.TryGetValue(overflow.SessionId, out var channel))
            {
                channel.Writer.TryComplete(overflow);
            }
            if (_activeCompletions.TryGetValue(overflow.SessionId, out var tcs))
            {
                tcs.TrySetException(overflow);
            }
        });

        connection.On<string>("ReceiveSessionClosed", sessionId =>
        {
            if (_activeChannels.TryRemove(sessionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
            if (_activeCompletions.TryRemove(sessionId, out var tcs))
            {
                tcs.TrySetResult();
            }
        });

        connection.On<int>("ReceiveQueuePosition", position =>
        {
            QueuePositionReceived?.Invoke(position);
        });

        connection.On<string, string, string>("ReceiveLog", (level, category, message) =>
        {
            LogReceived?.Invoke(level, category, message);
        });

        connection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, _) =>
        {
            _onMetricsCallback?.Invoke(metrics);
        });

        connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
        {
            _onDownloadProgressCallback?.Invoke(progress);
        });
    }

    #region Data Plane & Inference

    public async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId,
        string repoId,
        ChatMessage deltaMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        // Захватываем локальную ссылку с явной проверкой на null
        var connection = _hubConnection
            ?? throw new InvalidOperationException("SignalR Hub connection is not established.");

        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _activeChannels[sessionId] = channel;
        _activeCompletions[sessionId] = tcs;

        try
        {
            await connection.InvokeAsync("JoinSession", sessionId, repoId, ct);

            await connection.SendAsync("SendPromptDelta", sessionId, deltaMessage, ct);

            await foreach (var token in channel.Reader.ReadAllAsync(ct))
            {
                yield return token;
            }

            await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _activeChannels.TryRemove(sessionId, out _);
            _activeCompletions.TryRemove(sessionId, out _);
        }
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("LeaveSession", sessionId, ct);
            }
            catch (Exception)
            {
                // Graceful ignore if connection or session already terminated
            }
        }
    }

    public async Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return await _hubConnection!.InvokeAsync<int>("GetSessionTokenCount", sessionId, ct);
    }

    public async Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("RollbackSession", sessionId, targetPosition, ct);
    }

    public async Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("ShiftSessionCache", sessionId, startPos, count, ct);
    }

    #endregion

    #region Control Plane & Model Management

    public async Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        return await _hubConnection!.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync", ct);
    }

    public async Task LoadModelAsync(string repoId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("LoadModelAsync", repoId, (string?)null, ct);
    }

    public async Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("UnloadModelAsync", repoId, ct);
    }

    public async Task SwapModelAsync(string repoId, string? profile = null, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("SwapModelAsync", repoId, profile, ct);
    }

    public async Task DownloadModelAsync(string repoId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("DownloadModelAsync", repoId, ct);
    }

    public async Task SubscribeToModelDownloadAsync(string repoId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("SubscribeToModelDownload", repoId, ct);
    }

    public async Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("UnsubscribeFromModelDownload", repoId, ct);
    }

    #endregion

    #region Observability & Telemetry

    public async Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics,
        Action<DownloadProgress> onSsrProgress,
        CancellationToken ct)
    {
        _onMetricsCallback = onMetrics;
        _onDownloadProgressCallback = onSsrProgress;
        await EnsureConnectedAsync(ct);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}