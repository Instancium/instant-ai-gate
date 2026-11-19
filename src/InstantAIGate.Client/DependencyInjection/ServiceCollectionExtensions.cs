namespace InstantAIGate.Client.DependencyInjection;

using System;
using InstantAIGate.Client.Chat;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.Embeddings;
using InstantAIGate.Client.Telemetry;
using InstantAIGate.Client.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Dependency injection registration extensions for <c>InstantAIGate.Client</c>.
/// Wires the shared SignalR connection and capability-segregated clients as singletons
/// so all consumers share a single authenticated hub transport.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GatewayOptions"/>, <see cref="IGatewayConnection"/>, <see cref="IChatClientFactory"/>,
    /// <see cref="IGatewayEmbeddingClient"/>, and <see cref="IGatewayTelemetryClient"/> in the service collection.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">Callback used to configure <see cref="GatewayOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> for fluent chaining.</returns>
    public static IServiceCollection AddInstantAIGateClient(
        this IServiceCollection services,
        Action<GatewayOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GatewayOptions>()
            .Configure(configure)
            .PostConfigure(options => options.Validate());

        services.TryAddSingleton<IGatewayConnection, GatewayConnection>();
        services.TryAddSingleton<IChatClientFactory, GatewayChatClientFactory>();
        services.TryAddSingleton<IGatewayEmbeddingClient, GatewayEmbeddingClient>();
        services.TryAddSingleton<IGatewayTelemetryClient, GatewayTelemetryClient>();

        return services;
    }
}
