using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli.Core;

public interface IGatewayClient
{
    IAsyncEnumerable<string> StreamChatAsync(string repoId, ChatMessage message, CancellationToken ct);
    Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct);
}