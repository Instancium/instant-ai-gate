namespace InstantAIGate.Core.Interfaces.Hub;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Dtos.Inference;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Defines the server-side hub contract for administrative tasks (Control Plane).
/// Facilitates VRAM pool management, type-scoped model hot-swapping, queue limits, and dashboard telemetry.
/// Requires 'GatewayAdmin' policy.
/// </summary>
public interface IGatewayHubAdmin
{
    /// <summary>
    /// Loads a model into the VRAM pool alongside existing models.
    /// </summary>
    /// <param name="repoId">The model repository identifier.</param>
    /// <param name="profileName">Optional hardware profile name. Embedding models are auto-routed to the 'Embedding' profile.</param>
    Task LoadModelAsync(string repoId, string? profileName = null);

    /// <summary>
    /// Performs a type-scoped hot-swap: unloads existing models of the same type, then loads the new model.
    /// </summary>
    /// <param name="repoId">The new model repository identifier.</param>
    /// <param name="profileName">Optional hardware profile name.</param>
    Task SwapModelAsync(string repoId, string? profileName = null);

    /// <summary>
    /// Gracefully drains and unloads a specific model from VRAM, leaving other models active.
    /// </summary>
    /// <param name="repoId">The model repository identifier.</param>
    Task UnloadModelAsync(string repoId);

    /// <summary>
    /// Enqueues a background download job for a model from the configured catalog.
    /// </summary>
    /// <param name="repoId">The model repository identifier.</param>
    Task DownloadModelAsync(string repoId);

    /// <summary>
    /// Forcefully purges pooled idle contexts from VRAM.
    /// </summary>
    /// <param name="repoId">The target model ID. If empty, purges idle contexts across all loaded models.</param>
    Task PurgeIdleContextsAsync(string repoId);

    /// <summary>
    /// Adjusts the dynamic inference request queue concurrency limit.
    /// </summary>
    /// <param name="limit">The new maximum queue size.</param>
    Task SetQueueLimitAsync(int limit);

    /// <summary>
    /// Explicitly triggers a managed garbage collection (LOH/SOH) across the server.
    /// </summary>
    Task TriggerGarbageCollectionAsync();

    /// <summary>
    /// Forcibly destroys a hanging session, immediately releasing its VRAM slot and native context.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to kill.</param>
    Task ForceKillSessionAsync(string sessionId);

    /// <summary>
    /// Retrieves the status registry for all models known to the active session manager.
    /// </summary>
    /// <returns>A collection of registry status objects.</returns>
    Task<IEnumerable<ModelRegistryStatus>> GetModelsAsync();

    /// <summary>
    /// Retrieves current telemetry for the inference queue, including active leases and pending requests.
    /// </summary>
    /// <returns>Current queue metrics.</returns>
    Task<InferenceMetrics> GetQueueMetricsAsync();

    /// <summary>
    /// Retrieves a comprehensive point-in-time snapshot of server hardware, memory, and model telemetry.
    /// </summary>
    /// <returns>The complete dashboard snapshot.</returns>
    Task<DashboardTelemetrySnapshot> GetDashboardSnapshotAsync();

    /// <summary>
    /// Subscribes the caller to the 1Hz telemetry broadcast group for dashboard monitoring.
    /// </summary>
    Task SubscribeToDashboardTelemetry();

    /// <summary>
    /// Unsubscribes the caller from the dashboard telemetry broadcast group.
    /// </summary>
    Task UnsubscribeFromDashboardTelemetry();

    /// <summary>
    /// Retrieves detailed telemetry for all currently active inference sessions.
    /// </summary>
    /// <returns>A list of session telemetry snapshots.</returns>
    Task<IReadOnlyList<SessionTelemetryDto>> GetActiveSessionsAsync();
}