using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Domain.Dtos.Config;

namespace InstantAIGate.Application.Interfaces.Inference
{
    /// <summary>
    /// Defines the contract for managing model lifecycle, context pooling, and inference resources.
    /// </summary>
    public interface IModelProvider : IDisposable
    {
        Task InitializeAsync(ModelSettings config, CancellationToken ct = default);

        void UnloadModel(string modelPath);

        bool IsLoaded(string modelPath);

        /// <summary>
        /// Retrieves an inference context from the pool or creates a new one.
        /// </summary>
        Task<IInferenceContext> GetContextAsync(string modelPath, CancellationToken ct = default);

        /// <summary>
        /// Retrieves weights using strict abstraction. No leaking of 3rd party types.
        /// </summary>
        Task<IInferenceModel> GetWeightsAsync(string modelPath, CancellationToken ct = default);

        IEnumerable<NativeModelDetails> GetNativeDetails();
        IEnumerable<ModelRegistryStatus> GetStatus();
    }
}