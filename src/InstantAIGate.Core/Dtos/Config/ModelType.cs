namespace InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Classification of model architecture and inference mode.
/// </summary>
public enum ModelType
{
    /// <summary>
    /// Standard text-only large language model.
    /// </summary>
    Llm = 0,

    /// <summary>
    /// Vision-language model with multimodal capabilities (e.g., Qwen-VL, LLaVA).
    /// </summary>
    Vlm = 1,

    /// <summary>
    /// Embedding model for vector representations.
    /// </summary>
    Embedding = 2,

    /// <summary>
    /// Speech/audio model.
    /// </summary>
    Audio = 3
}