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
    /// <summary>
    /// Creates or resumes a stateful chat session bound to a specific model in the VRAM pool.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <param name="repoId">The target model ID. If empty, the primary generative model is used.</param>
    Task JoinSession(string sessionId, string repoId);

    /// <summary>
    /// Soft-releases the session, clearing its KV-cache and returning the native context to the model's pool.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    Task LeaveSession(string sessionId);

    /// <summary>
    /// Hard-destroys the session, permanently freeing the native context slot from VRAM.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    Task DestroySession(string sessionId);

    /// <summary>
    /// Submits a prompt delta and initiates token streaming for the specified session.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <param name="deltaMessage">The multimodal chat message to process.</param>
    Task SendPromptDelta(string sessionId, ChatMessage deltaMessage);

    /// <summary>
    /// Truncates the session KV-cache to a specific token boundary.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <param name="targetPosition">The token index to rollback to.</param>
    Task RollbackSession(string sessionId, int targetPosition);

    /// <summary>
    /// Applies a sliding-window eviction to the session KV-cache, preserving the system prefix.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <param name="startPos">The starting token offset to remove.</param>
    /// <param name="count">The number of tokens to remove.</param>
    Task ShiftSessionCache(string sessionId, int startPos, int count);

    /// <summary>
    /// Retrieves the current token count (PastTokens) stored in the session's KV-cache.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <returns>The number of tokens currently cached.</returns>
    Task<int> GetSessionTokenCount(string sessionId);

    /// <summary>
    /// Retrieves an immutable point-in-time snapshot of the gateway's operational state.
    /// </summary>
    /// <returns>The detailed operational status.</returns>
    Task<GatewayStatusDetails> GetGatewayStatus();

    /// <summary>
    /// Retrieves hardware and configuration details for the primary active chat model.
    /// </summary>
    /// <returns>Details of the primary active model.</returns>
    Task<NativeModelDetails> GetActiveModelDetailsAsync();

    /// <summary>
    /// Retrieves details for all models currently loaded and ready in the VRAM pool.
    /// </summary>
    /// <returns>A collection of native model details.</returns>
    Task<IEnumerable<NativeModelDetails>> GetLoadedModelsDetailsAsync();

    /// <summary>
    /// Subscribes the caller to real-time download progress updates for a specific model.
    /// </summary>
    /// <param name="repoId">The model repository identifier.</param>
    Task SubscribeToModelDownload(string repoId);

    /// <summary>
    /// Unsubscribes the caller from real-time download progress updates for a specific model.
    /// </summary>
    /// <param name="repoId">The model repository identifier.</param>
    Task UnsubscribeFromModelDownload(string repoId);

    /// <summary>
    /// Generates semantic vector embeddings for the provided text. Auto-routes to an embedding model if necessary.
    /// </summary>
    /// <param name="repoId">The target model ID. If empty, the gateway resolves the active embedding model.</param>
    /// <param name="request">The embedding generation request parameters.</param>
    /// <returns>The generated vector and token count.</returns>
    Task<EmbeddingResponse> GenerateEmbeddingsAsync(string repoId, EmbeddingRequest request);

    /// <summary>
    /// Queries the embedding dimension (n_embd) of the specified model.
    /// </summary>
    /// <param name="repoId">The target model ID. If empty, the gateway resolves the active embedding model.</param>
    /// <returns>The size of the embedding vector.</returns>
    Task<int> GetEmbeddingDimensionAsync(string repoId);
}