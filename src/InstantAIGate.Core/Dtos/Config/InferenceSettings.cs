namespace InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Configuration for inference execution parameters.
/// </summary>
public record InferenceSettings
{
    /// <summary>
    /// Maximum number of tokens to generate.
    /// </summary>
    public int MaxTokens { get; init; } = 512;

    /// <summary>
    /// Temperature for sampling.
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// Top-p sampling parameter.
    /// </summary>
    public float TopP { get; init; } = 0.9f;

    /// <summary>
    /// Top-k sampling parameter.
    /// </summary>
    public int TopK { get; init; } = 40;

    /// <summary>
    /// Repeat penalty for generation.
    /// </summary>
    public float RepeatPenalty { get; init; } = 1.1f;
    /// <summary>
    /// The number of recent tokens to check for repetitions. 
    /// </summary>
    public int PenaltyLastN { get; init; } = 64;

    public int BatchSize { get; set; } = 512;
    public uint? Seed { get; set; }

}