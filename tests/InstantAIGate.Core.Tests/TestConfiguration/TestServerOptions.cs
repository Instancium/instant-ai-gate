namespace InstantAIGate.Core.Tests.TestConfiguration;

/// <summary>
/// Typed addresses and secrets for the test server stand.
/// Mapped from the "InstantAIGate:TestServer" section of the test project appsettings.json.
/// </summary>
public sealed class TestServerOptions
{
    public const string SectionName = "InstantAIGate:TestServer";

    /// <summary>Public base URL used by test clients.</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>Admin base URL used by test clients.</summary>
    public string AdminBaseUrl { get; init; } = "http://localhost:5001";

    /// <summary>Kestrel endpoint URL for the public API on the test server.</summary>
    public string PublicEndpointUrl { get; init; } = "http://0.0.0.0:5000";

    /// <summary>Kestrel endpoint URL for the admin API on the test server.</summary>
    public string AdminEndpointUrl { get; init; } = "http://0.0.0.0:5001";

    /// <summary>Relative path of the SignalR telemetry hub.</summary>
    public string TelemetryHubPath { get; init; } = "/hub/telemetry";

    /// <summary>Admin API token used in tests.</summary>
    public string AdminApiKey { get; init; } = "test-admin-secret";

    /// <summary>Tenant API token used against the public API in tests.</summary>
    public string TenantApiKey { get; init; } = "test-tenant-123";

    /// <summary>Invalid token used by negative security tests.</summary>
    public string InvalidApiKey { get; init; } = "wrong-secret";

    /// <summary>Full telemetry hub URL for SignalR clients.</summary>
    public Uri TelemetryHubUrl => new(new Uri(AdminBaseUrl), TelemetryHubPath);
}
