namespace InstantAIGate.Core.Interfaces.Hub;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Defines the server-side hub contract exposed to remote clients (Data Plane).
/// Facilitates session lifecycle, chat streaming, embeddings, and model pool discovery.
/// Requires 'GatewayUser' policy.
/// </summary>
public interface IGatewayHub
{
    // Session Lifecycle & Chat Streaming
    Task JoinSession(string sessionId, string repoId);
    Task LeaveSession(string sessionId);
    Task DestroySession(string sessionId);
    Task SendPromptDelta(string sessionId, ChatMessage deltaMessage);

    // Context KV-Cache Management
    Task RollbackSession(string sessionId, int targetPosition);
    Task ShiftSessionCache(string sessionId, int startPos, int count);
    Task<int> GetSessionTokenCount(string sessionId);

    // Telemetry & Discovery
    Task<GatewayStatusDetails> GetGatewayStatus();
    Task<NativeModelDetails> GetActiveModelDetailsAsync();
    Task<IEnumerable<NativeModelDetails>> GetLoadedModelsDetailsAsync();
    Task SubscribeToModelDownload(string repoId);
    Task UnsubscribeFromModelDownload(string repoId);

    // Stateless Embeddings (Auto-routed)
    Task<EmbeddingResponse> GenerateEmbeddingsAsync(string repoId, EmbeddingRequest request);
    Task<int> GetEmbeddingDimensionAsync(string repoId);
}