using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Domain.Dtos.Config;

namespace InstantAIGate.Application.Interfaces.Inference
{
    /// <summary>
    /// Manages the lifecycle of active models in memory and coordinates safe, concurrent access to native inference resources.
    /// Acts as the central orchestrator for resource allocation, semaphore-based throttling, and operational state tracking.
    /// </summary>
    public interface IModelManager : IDisposable
    {
        /// <summary>
        /// Explicitly loads and activates a model in memory based on the provided configuration,
        /// initializing its native weight layers, context pools, and concurrency throttles.
        /// </summary>
        /// <param name="config">The model load specifications (file path, maximum context limits, hardware thread allocations, and GPU layers).</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        Task LoadModelAsync(ModelSettings config, CancellationToken ct = default);

        /// <summary>
        /// Evicts the specified model from memory, performing a clean, forced teardown of all allocated native execution contexts, 
        /// model weights, backing pools, and semaphores from VRAM/RAM.
        /// </summary>
        /// <param name="modelPath">The unique identifier or physical storage path of the model to be evicted.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        Task UnloadModelAsync(string modelPath, CancellationToken ct = default);

        /// <summary>
        /// Retrieves the identifiers (RepoIds/Paths) of all models currently active in memory and ready to process client requests.
        /// Directly feeds the OpenAI-compliant `/v1/models` endpoint payload.
        /// </summary>
        IEnumerable<string> GetActiveModels();

        IEnumerable<ModelRegistryStatus> GetActiveModelsStatus();
        IEnumerable<NativeModelDetails> GetNativeDetails();

        /// <summary>
        /// Provides read-only access to the internal registry of currently loaded model configurations.
        /// Useful for telemetry services to derive active load parameters without modifying manager state.
        /// </summary>
        IReadOnlyDictionary<string, ModelSettings> ActiveModels { get; }

        /// <summary>
        /// Provides read-only access to the concurrency throttle registry. 
        /// Allows external monitoring of semaphore states (CurrentCount/Available slots) to accurately calculate real-time usage.
        /// </summary>
        IReadOnlyDictionary<string, SemaphoreSlim> UserSemaphores { get; }


    }
}