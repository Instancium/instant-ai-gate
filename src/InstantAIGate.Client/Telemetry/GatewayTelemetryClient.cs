namespace InstantAIGate.Client.Telemetry;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// SignalR-backed <see cref="IGatewayTelemetryClient"/> wrapping read-only Data Plane
/// diagnostic methods exposed by <see cref="Core.Interfaces.Hub.IGatewayHub"/>.
/// </summary>
public sealed class GatewayTelemetryClient : IGatewayTelemetryClient
{
    private readonly IGatewayConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayTelemetryClient"/> class.
    /// </summary>
    /// <param name="connection">The shared gateway SignalR connection.</param>
    public GatewayTelemetryClient(IGatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    public async Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default)
    {
        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        return await hub.InvokeAsync<GatewayStatusDetails>(
            GatewayHubMethods.GetGatewayStatus,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken cancellationToken = default)
    {
        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        return await hub.InvokeAsync<NativeModelDetails>(
            GatewayHubMethods.GetActiveModelDetailsAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NativeModelDetails>> GetLoadedModelsDetailsAsync(CancellationToken cancellationToken = default)
    {
        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        var models = await hub.InvokeAsync<IEnumerable<NativeModelDetails>>(
            GatewayHubMethods.GetLoadedModelsDetailsAsync,
            cancellationToken).ConfigureAwait(false);

        return models as IReadOnlyList<NativeModelDetails> ?? models.ToArray();
    }

    /// <inheritdoc />
    public async Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        return await hub.InvokeAsync<int>(
            GatewayHubMethods.GetSessionTokenCount,
            sessionId,
            cancellationToken).ConfigureAwait(false);
    }
}
