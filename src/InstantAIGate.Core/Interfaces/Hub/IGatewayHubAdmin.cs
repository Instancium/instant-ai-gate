namespace InstantAIGate.Core.Interfaces.Hub;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Dtos.Inference;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Defines the server-side hub contract for administrative tasks (Control Plane).
/// Facilitates VRAM pool management, type-scoped model hot-swapping, and queue limits.
/// Requires 'GatewayAdmin' policy.
/// </summary>
public interface IGatewayHubAdmin
{
    Task LoadModelAsync(string repoId, string? profileName = null);
    Task SwapModelAsync(string repoId, string? profileName = null);
    Task UnloadModelAsync(string repoId);
    Task DownloadModelAsync(string repoId);
    Task PurgeIdleContextsAsync(string repoId);
    Task SetQueueLimitAsync(int limit);
    Task<IEnumerable<ModelRegistryStatus>> GetModelsAsync();
    Task<InferenceMetrics> GetQueueMetricsAsync();
}
