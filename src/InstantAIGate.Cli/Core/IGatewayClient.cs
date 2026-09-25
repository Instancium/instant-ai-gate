using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;

namespace InstantAIGate.Cli.Core;

public interface IGatewayClient
{
    IAsyncEnumerable<string> StreamChatAsync(string repoId, IEnumerable<ChatMessage> messages, CancellationToken ct);
    Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct);
    Task LoadModelAsync(string repoId, CancellationToken ct = default);
}