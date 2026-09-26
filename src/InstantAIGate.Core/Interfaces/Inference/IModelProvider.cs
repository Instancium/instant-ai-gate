namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Manages model loading, context pooling, and inference lifecycle.
/// </summary>
public interface IModelProvider : IDisposable
{
    /// <summary>
    /// Checks if a model is currently loaded.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <returns>True if model is loaded, false otherwise.</returns>
    bool IsLoaded(string repoId);

    /// <summary>
    /// Initializes and loads a model with the specified configuration.
    /// </summary>
    /// <param name="config">Model configuration settings.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(ModelSettings config, ResolvedModelPaths resolvedPaths, CancellationToken ct = default);

    /// <summary>
    /// Gets an inference context for the specified model.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inference context wrapper.</returns>
    Task<InferenceContext> GetInferenceContextAsync(string repoId, CancellationToken ct = default);

    /// <summary>
    /// Gets model weights for direct access.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Model weights accessor.</returns>
    Task<ModelWeights> GetWeightsAsync(string repoId, CancellationToken ct = default);

    /// <summary>
    /// Unloads the specified model from memory.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    void UnloadModel(string repoId);

    /// <summary>
    /// Gets the status of all loaded models.
    /// </summary>
    IEnumerable<ModelRegistryStatus> GetStatus();

    /// <summary>
    /// Gets native backend details for all loaded models.
    /// </summary>
    IEnumerable<NativeModelDetails> GetNativeDetails();
}