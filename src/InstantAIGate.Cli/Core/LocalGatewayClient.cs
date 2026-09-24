using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli.Core;

public class LocalGatewayClient : IGatewayClient
{
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IModelManager _modelManager;

    public LocalGatewayClient(IInferenceEngine inferenceEngine, IModelManager modelManager)
    {
        _inferenceEngine = inferenceEngine;
        _modelManager = modelManager;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string repoId,
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = new InferenceSettings { MaxTokens = 512, Temperature = 0.7f };
        var parts = new List<MessageContent>(); // Will be populated from TuiDashboardState.PendingMedia later

        await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(repoId, message.Content, parts, settings, ct))
        {
            yield return chunk;
        }
    }

    public Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics,
        Action<DownloadProgress> onSsrProgress,
        CancellationToken ct)
    {
        // For local mode, we poll the ModelManager periodically (similar to the Server's BackgroundService)
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var metrics = _modelManager.GetMetrics();
                    onMetrics(metrics);
                }
                catch { /* Ignore */ }

                await Task.Delay(1000, ct); // 1Hz refresh
            }
        }, ct);

        return Task.CompletedTask;
    }
}