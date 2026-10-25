namespace InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Request payload for generating text embeddings.
/// Carries the input text and optional normalization flag used by embedding models
/// such as Qwen3-Embedding to produce dense semantic vectors.
/// </summary>
public sealed record EmbeddingRequest(
    string Text,
    bool Normalize = true,
    int? MaxTokens = null
);