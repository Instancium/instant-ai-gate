namespace InstantAIGate.Core.Tests.TestConfiguration;

using System;

public sealed class TestServerOptions
{
    public const string SectionName = "InstantAIGate:TestServer";

    public string PublicBaseUrl { get; init; } = "http://localhost:5000";
    public string PublicEndpointUrl { get; init; } = "http://0.0.0.0:5000";
    public string TelemetryHubPath { get; init; } = "/hub/telemetry";
    public string AdminApiKey { get; init; } = "test-admin-secret";
    public string TenantApiKey { get; init; } = "test-tenant-123";
    public string InvalidApiKey { get; init; } = "wrong-secret";

    public Uri TelemetryHubUrl => new(new Uri(PublicBaseUrl), TelemetryHubPath);
}