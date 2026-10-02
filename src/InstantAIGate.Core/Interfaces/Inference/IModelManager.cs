namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IModelManager : IDisposable
{
    Task LoadModelAsync(ModelSettings config, CancellationToken ct = default);
    Task SwapModelAsync(ModelSettings newConfig, CancellationToken ct = default);
    Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default);
    Task UnloadModelAsync(string repoId, CancellationToken ct = default);
    Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default);
    ModelSettings? GetActiveSettings();
    NativeModelDetails GetActiveModelDetails();
    InferenceMetrics GetMetrics();
    IEnumerable<ModelRegistryStatus> GetActiveModelsStatus();
    IEnumerable<string> GetActiveModels();
    IEnumerable<NativeModelDetails> GetNativeDetails();
}