namespace InstantAIGate.Cli.Configuration;

public class RemoteGatewaySettings
{
    public string PublicUrl { get; set; } = "http://localhost:5000";
    public string AdminHubUrl { get; set; } = "http://localhost:5001/hub/telemetry";
    public string AdminKey { get; set; } = "test-admin-secret";
}