using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli.Core;

public class RemoteGatewayClient : IGatewayClient, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _adminHubUrl;
    private readonly string _adminKey;
    private HubConnection? _hubConnection;

    public RemoteGatewayClient(HttpClient httpClient, string serverUrl, string adminKey)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(serverUrl);
        _adminHubUrl = $"{serverUrl.Replace("5000", "5001")}/hub/telemetry"; // Assumes standard dual-port setup
        _adminKey = adminKey;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
         string repoId,
         ChatMessage message,
         [EnumeratorCancellation] CancellationToken ct)
    {
        var requestPayload = new
        {
            model = repoId,
            messages = new[] { new { role = message.Role, content = message.Content } },
            stream = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(requestPayload)
        };
        // In real app, append bearer token for public endpoint if required

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // ReadLineAsync returns null at the end of the stream.
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);

            if (line == null)
                break; // End of stream reached

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentElement))
                {
                    yield return contentElement.GetString() ?? string.Empty;
                }
            }
        }
    }

    public async Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics,
        Action<DownloadProgress> onSsrProgress,
        CancellationToken ct)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_adminHubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_adminKey)!;
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, _) =>
        {
            onMetrics(metrics);
        });

        _hubConnection.On<DownloadProgress>("ReceiveSsrProgress", progress =>
        {
            onSsrProgress(progress);
        });

        await _hubConnection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
    }
}