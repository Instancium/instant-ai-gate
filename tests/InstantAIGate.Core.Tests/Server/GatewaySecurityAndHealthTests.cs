using System.Net;
using System.Net.Http.Headers;
using InstantAIGate.Core.Tests.TestConfiguration;

namespace InstantAIGate.Core.Tests.Server;

public class GatewaySecurityAndHealthTests : IClassFixture<GatewayTestFixture>
{
    private readonly HttpClient _publicClient;
    private readonly HttpClient _adminClient;
    private readonly TestServerOptions _serverOptions;

    public GatewaySecurityAndHealthTests(GatewayTestFixture fixture)
    {
        _serverOptions = fixture.ServerOptions;

        // Client emulating requests to the public port (from test configuration)
        _publicClient = fixture.CreatePublicClient();

        // Client emulating requests to the admin port (from test configuration)
        _adminClient = fixture.CreateAdminClient();
    }

    [Fact]
    public async Task HealthLive_ShouldReturn200OK_WithoutAuth_OnBothPorts()
    {
        var publicResponse = await _publicClient.GetAsync("/health/live");
        var adminResponse = await _adminClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ShouldReturn503_WhenNoModelLoaded()
    {
        var response = await _publicClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task PublicApi_ShouldReturn401_WhenMissingApiKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        var response = await _publicClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_ShouldReturn403_WhenAccessedViaPublicPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.AdminApiKey);

        var response = await _publicClient.SendAsync(request);

        // PortRoutingMiddleware should block requests to /admin via the public port
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicApi_ShouldReturn403_WhenAccessedViaAdminPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.TenantApiKey);

        var response = await _adminClient.SendAsync(request);

        // PortRoutingMiddleware should block requests to /v1 via the admin port
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_ShouldReturn200_WithValidAdminKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.AdminApiKey);

        var response = await _adminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_ShouldReturn401_WithInvalidAdminKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.InvalidApiKey);

        var response = await _adminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
