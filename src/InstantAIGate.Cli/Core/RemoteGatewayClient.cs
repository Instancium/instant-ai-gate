namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class RemoteGatewayClient : IGatewayClient, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _adminHubUrl;
    private readonly string _adminKey;
    private HubConnection? _hubConnection;

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


    public async IAsyncEnumerable<string> StreamChatAsync(string repoId, IEnumerable<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        var messagesPayload = messages.Select(m => new
        {
            role = m.Role,
            content = SerializeMessageContent(m)
        }).ToArray();

        var requestPayload = new
        {
            // FIX: Pass the repoId exactly as is (empty string). 
            // The server's ChatCompletionsController will bypass the strict matching 
            // if the model string is empty, automatically using the active model in VRAM.
            model = repoId,
            messages = messagesPayload,
            stream = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(requestPayload)
        };

        if (!string.IsNullOrWhiteSpace(_adminKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminKey);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

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

    private static object SerializeMessageContent(ChatMessage message)
    {
        if (!message.Parts.Any(p => p is not TextContent))
        {
            return message.Content;
        }

        var partsList = new List<object>();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case TextContent tc:
                    partsList.Add(new { type = "text", text = tc.Text });
                    break;

                case ImageFileContent ifc:
                    if (!File.Exists(ifc.FilePath))
                    {
                        throw new FileNotFoundException($"Image file not found: {ifc.FilePath}");
                    }

                    string ext = Path.GetExtension(ifc.FilePath).TrimStart('.').ToLowerInvariant();
                    if (ext == "jpg") ext = "jpeg";

                    byte[] imageBytes = File.ReadAllBytes(ifc.FilePath);
                    string base64 = Convert.ToBase64String(imageBytes);
                    string dataUrl = $"data:image/{ext};base64,{base64}";

                    partsList.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = dataUrl }
                    });
                    break;

                case ImageUrlContent iuc:
                    partsList.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = iuc.Url }
                    });
                    break;
            }
        }
        return partsList;
    }

    public Task LoadModelAsync(string repoId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
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

        _hubConnection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, _) => onMetrics(metrics));
        _hubConnection.On<DownloadProgress>("ReceiveSsrProgress", progress => onSsrProgress(progress));

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