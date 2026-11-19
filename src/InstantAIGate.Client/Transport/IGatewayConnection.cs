namespace InstantAIGate.Client.Transport;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Manages the shared SignalR transport connection to the InstantAIGate hub and gates
/// inference operations on gateway readiness. Isolating transport concerns here keeps
/// capability clients focused on their domain contracts.
/// </summary>
public interface IGatewayConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the validated options used to configure this connection.
    /// </summary>
    GatewayOptions Options { get; }

    /// <summary>
    /// Ensures the underlying SignalR <see cref="HubConnection"/> is initialized and connected.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <returns>The connected <see cref="HubConnection"/> instance.</returns>
    Task<HubConnection> GetConnectedHubAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the connection is established and verifies that the remote gateway reports
    /// <see cref="GatewayOperationalStatus.Ready"/> before dispatching inference work.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the readiness check.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the gateway is not in the <see cref="GatewayOperationalStatus.Ready"/> state.
    /// </exception>
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}
