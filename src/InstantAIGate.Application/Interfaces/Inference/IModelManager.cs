using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Domain.Dtos.Config;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Application.Interfaces.Inference
{
    /// <summary>
    /// Temporary compatibility interface for ONNX Runtime migration.
    /// Allows TelemetryService and controllers to function without breaking the DI container.
    /// </summary>
    public interface IModelManager
    {
        Task LoadModelAsync(ModelSettings config, CancellationToken ct = default);
        Task UnloadModelAsync(string repoId, CancellationToken ct = default);

        Task<IInferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default);

        IEnumerable<string> GetActiveModels();
        ModelSettings? GetActiveSettings();
        IEnumerable<ModelRegistryStatus> GetActiveModelsStatus();
        IEnumerable<NativeModelDetails> GetNativeDetails();
        InferenceMetrics GetMetrics();
    }
}