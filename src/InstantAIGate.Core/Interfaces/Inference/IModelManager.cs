namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Coordinates the single-model state, graceful draining, and context acquisition.
/// </summary>
public interface IModelManager : IDisposable
{
    /// <summary>
    /// Loads a model with the specified configuration.
    /// </summary>
    /// <param name="config">Model configuration settings.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LoadModelAsync(ModelSettings config, CancellationToken ct = default);

    /// <summary>
    /// Performs a graceful hot-swap to a new model configuration.
    /// </summary>
    /// <param name="newConfig">New model configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SwapModelAsync(ModelSettings newConfig, CancellationToken ct = default);

    /// <summary>
    /// Acquires an inference context for the specified model.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inference context wrapper.</returns>
    Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default);

    /// <summary>
    /// Unloads the specified model from memory.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnloadModelAsync(string repoId, CancellationToken ct = default);

    /// <summary>
    /// Acquires model weights for direct access.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Model weights accessor.</returns>
    Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default);

    /// <summary>
    /// Gets the configuration of the currently active model.
    /// </summary>
    ModelSettings? GetActiveSettings();

    /// <summary>
    /// Gets the current throughput and queue metrics for telemetry.
    /// </summary>
    InferenceMetrics GetMetrics();

    /// <summary>
    /// Gets the status of all active models.
    /// </summary>
    IEnumerable<ModelRegistryStatus> GetActiveModelsStatus();

    /// <summary>
    /// Gets the list of active model repository identifiers.
    /// </summary>
    IEnumerable<string> GetActiveModels();

    /// <summary>
    /// Gets native backend details for all loaded models.
    /// </summary>
    IEnumerable<NativeModelDetails> GetNativeDetails();
}