namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IGatewayClient
{
    // Data Plane
    IAsyncEnumerable<string> StreamChatAsync(string sessionId, string repoId, ChatMessage deltaMessage, CancellationToken ct);
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);
    Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default);
    Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default);
    Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default);

    // Control Plane (Models & Management)
    Task LoadModelAsync(string repoId, CancellationToken ct = default);
    Task UnloadModelAsync(string repoId, CancellationToken ct = default);
    Task SwapModelAsync(string repoId, string? profile = null, CancellationToken ct = default);
    Task DownloadModelAsync(string repoId, CancellationToken ct = default);
    Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default);

    // Observability & Groups
    Task ConnectTelemetryAsync(Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct);
    Task SubscribeToModelDownloadAsync(string repoId, CancellationToken ct = default);
    Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken ct = default);

    // Client Events
    event Action<int>? QueuePositionReceived;
    event Action<string, string, string>? LogReceived; // level, category, message
}