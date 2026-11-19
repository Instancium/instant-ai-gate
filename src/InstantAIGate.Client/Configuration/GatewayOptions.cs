namespace InstantAIGate.Client.Configuration;

using System;

/// <summary>
/// Configuration options for connecting to and authenticating with an InstantAIGate server.
/// Centralizing endpoint, identity, and model routing settings ensures all capability clients
/// share a consistent transport configuration.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>
    /// Gets or sets the base HTTP or HTTPS URL of the InstantAIGate server (for example, <c>http://localhost:5000</c>).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative SignalR hub path exposed by the gateway Data Plane.
    /// Defaults to <c>/hub/gateway</c>.
    /// </summary>
    public string HubPath { get; set; } = "/hub/gateway";

    /// <summary>
    /// Gets or sets the Bearer API key used to authenticate SignalR connections with the gateway.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the logical consumer identifier used as the prefix for generated session IDs.
    /// Prevents consumer-specific naming leaks and helps correlate server-side session telemetry.
    /// Defaults to <c>client</c>.
    /// </summary>
    public string ClientId { get; set; } = "client";

    /// <summary>
    /// Gets or sets the optional target repository identifier for embedding requests.
    /// When empty, the gateway resolves its active embedding model automatically.
    /// </summary>
    public string EmbeddingRepoId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional target repository identifier for generative chat requests.
    /// When empty, the gateway routes to its primary generative model.
    /// </summary>
    public string LlmRepoId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional expected embedding vector dimension used for lazy client-side validation.
    /// When specified, mismatches against the server model dimension fail fast instead of corrupting downstream vector stores.
    /// </summary>
    public int? ExpectedEmbeddingDimension { get; set; }

    /// <summary>
    /// Gets or sets the default KV-cache context policy used when creating chat clients without an explicit override.
    /// </summary>
    public ChatContextPolicy ContextPolicy { get; set; } = ChatContextPolicy.Stateless;

    /// <summary>
    /// Gets or sets the reconnect backoff delays used by SignalR automatic reconnection.
    /// </summary>
    public TimeSpan[] ReconnectDelays { get; set; } =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    /// <summary>
    /// Validates the required configuration values and throws an <see cref="InvalidOperationException"/>
    /// if the options cannot produce a valid authenticated gateway connection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="BaseUrl"/> is invalid, <see cref="HubPath"/> is empty,
    /// <see cref="ApiKey"/> is missing, or <see cref="ClientId"/> is empty.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("GatewayOptions.BaseUrl must be a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(HubPath))
        {
            throw new InvalidOperationException("GatewayOptions.HubPath must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("GatewayOptions.ApiKey must be configured.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("GatewayOptions.ClientId must not be empty.");
        }

        if (ExpectedEmbeddingDimension is <= 0)
        {
            throw new InvalidOperationException("GatewayOptions.ExpectedEmbeddingDimension must be positive when specified.");
        }
    }

    /// <summary>
    /// Combines <see cref="BaseUrl"/> and <see cref="HubPath"/> into the absolute SignalR hub <see cref="Uri"/>.
    /// </summary>
    /// <returns>The resolved absolute hub URI.</returns>
    public Uri GetHubUrl()
    {
        Validate();
        var trimmedBase = BaseUrl.TrimEnd('/');
        var normalizedPath = HubPath.StartsWith('/') ? HubPath : "/" + HubPath;
        return new Uri($"{trimmedBase}{normalizedPath}", UriKind.Absolute);
    }
}
