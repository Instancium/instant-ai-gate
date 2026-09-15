namespace InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Configuration for native backend initialization and runtime paths.
/// </summary>
public record BackendSettings
{
    /// <summary>
    /// Path to the native runtime libraries.
    /// </summary>
    public string RuntimePath { get; init; } = string.Empty;

    /// <summary>
    /// Preferred backend type (e.g., Vulkan, CPU, CUDA).
    /// </summary>
    public string PreferredBackend { get; init; } = "auto";

    /// <summary>
    /// Whether to enable verbose native logging.
    /// </summary>
    public bool VerboseLogging { get; init; }
}