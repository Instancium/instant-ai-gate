namespace InstantAIGate.Core.Tests.TestConfiguration;

using System;

public sealed class TestServerOptions
{
    public const string SectionName = "InstantAIGate:TestServer";
    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
    public string TenantApiKey { get; set; } = "tenant-test-token";
    public string AdminApiKey { get; set; } = "admin-test-token";

}