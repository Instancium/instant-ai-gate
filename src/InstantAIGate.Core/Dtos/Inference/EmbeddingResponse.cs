namespace InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Response payload returned after embedding generation.
/// The embedding vector is normalized to unit L2 length when requested by the caller.
/// </summary>
public sealed record EmbeddingResponse(
    float[] Embedding,
    int TokenCount,
    int EmbeddingDimension
);