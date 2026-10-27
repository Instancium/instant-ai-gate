namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IInferenceEngine
{
    Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default);
    Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamDeltaGenerationAsync(
        string sessionId,
        ChatMessage deltaMessage,
        InferenceSettings? overrideSettings = null,
        CancellationToken ct = default);

    /// <summary>
    /// Produces a dense semantic vector for the given text using an embedding-capable model.
    /// The returned vector is L2-normalized by default, suitable for cosine similarity search.
    /// </summary>
    /// <param name="modelId">Repository identifier of the embedding model.</param>
    /// <param name="request">Request payload containing the input text and normalization flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An embedding response with the vector, token count and dimension.</returns>
    Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        EmbeddingRequest request,
        CancellationToken ct = default);

    Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default);

}