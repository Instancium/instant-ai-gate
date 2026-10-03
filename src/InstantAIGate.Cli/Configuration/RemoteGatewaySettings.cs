namespace InstantAIGate.Cli.Configuration;

public class RemoteGatewaySettings
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string HubPath { get; set; } = "/hub/gateway";
    public string ApiKey { get; set; } = "test-admin-secret";

    public string HubUrl => $"{BaseUrl.TrimEnd('/')}/{HubPath.TrimStart('/')}";
}