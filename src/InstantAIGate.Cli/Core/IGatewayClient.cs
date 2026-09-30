namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IGatewayClient
{
    IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, CancellationToken ct);
    Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct);
    Task LoadModelAsync(string repoId, CancellationToken ct = default);
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);
}