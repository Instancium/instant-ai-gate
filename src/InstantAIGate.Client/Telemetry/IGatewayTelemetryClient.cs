namespace InstantAIGate.Client.Telemetry;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Read-only Data Plane telemetry contract for inspecting gateway readiness, loaded models,
/// and per-session KV-cache token consumption without exposing administrative mutations.
/// </summary>
public interface IGatewayTelemetryClient
{
    /// <summary>
    /// Retrieves an immutable snapshot of the gateway's operational state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the status query.</param>
    /// <returns>The current <see cref="GatewayStatusDetails"/> snapshot.</returns>
    Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves hardware and configuration details for the primary active chat model.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>Details of the primary active model.</returns>
    Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves details for all models currently loaded in the gateway VRAM pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>A read-only list of loaded <see cref="NativeModelDetails"/> entries.</returns>
    Task<IReadOnlyList<NativeModelDetails>> GetLoadedModelsDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current token count cached in the specified session's KV-cache.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The number of tokens currently stored in the session KV-cache.</returns>
    Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken cancellationToken = default);
}
