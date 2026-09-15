namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Provides abstraction over multimodal vision operations.
/// </summary>
public interface IVisionFacade
{
    /// <summary>
    /// Initializes a vision context for multimodal inference.
    /// </summary>
    /// <param name="projectorPath">Path to the vision projector file.</param>
    /// <param name="modelHandle">Handle to the base model.</param>
    /// <returns>Vision context wrapper.</returns>
    VisionContext InitializeContext(string projectorPath, IntPtr modelHandle);
}