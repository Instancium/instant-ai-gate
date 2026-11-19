namespace InstantAIGate.Client.Embeddings;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Capability contract for generating dense semantic vector embeddings via the gateway.
/// Returns <see cref="EmbeddingResponse"/> from <c>InstantAIGate.Core</c> directly so the SDK
/// remains free of third-party vector store dependencies.
/// </summary>
public interface IGatewayEmbeddingClient
{
    /// <summary>
    /// Generates a dense vector embedding for a single text input.
    /// </summary>
    /// <param name="text">The input text to embed.</param>
    /// <param name="normalize">Whether to L2-normalize the resulting vector.</param>
    /// <param name="maxTokens">Optional token limit override for truncation.</param>
    /// <param name="cancellationToken">A token to cancel the embedding request.</param>
    /// <returns>The generated <see cref="EmbeddingResponse"/> containing vector, token count, and dimension.</returns>
    Task<EmbeddingResponse> EmbedAsync(
        string text,
        bool normalize = true,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates dense vector embeddings sequentially for a batch of input texts.
    /// </summary>
    /// <param name="texts">The ordered collection of input texts to embed.</param>
    /// <param name="normalize">Whether to L2-normalize each resulting vector.</param>
    /// <param name="maxTokens">Optional token limit override for truncation.</param>
    /// <param name="cancellationToken">A token to cancel the batch operation.</param>
    /// <returns>An ordered list of <see cref="EmbeddingResponse"/> instances matching <paramref name="texts"/>.</returns>
    Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        bool normalize = true,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the active embedding model's vector dimension (<c>n_embd</c>) from the gateway.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The embedding vector dimension.</returns>
    Task<int> GetDimensionAsync(CancellationToken cancellationToken = default);
}
