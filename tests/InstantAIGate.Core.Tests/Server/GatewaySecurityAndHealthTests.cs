namespace InstantAIGate.Core.Tests.Server;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using InstantAIGate.Core.Tests.TestConfiguration;
using Xunit;

public class GatewaySecurityAndHealthTests : IClassFixture<GatewayTestFixture>
{
    private readonly HttpClient _client;
    private readonly TestServerOptions _serverOptions;

    public GatewaySecurityAndHealthTests(GatewayTestFixture fixture)
    {
        _serverOptions = fixture.ServerOptions;
        _client = fixture.CreatePublicClient();
    }

    [Fact]
    public async Task HealthLive_ShouldReturn200OK_WithoutAuth()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ShouldReturn503_WhenNoModelLoaded()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GatewayNegotiate_ShouldReturn401_WhenMissingToken()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/hub/gateway/negotiate?negotiateVersion=1");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GatewayNegotiate_ShouldReturn401_WhenQueryAccessTokenIsEmpty()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/hub/gateway/negotiate?negotiateVersion=1&access_token=");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GatewayNegotiate_ShouldReturn200_WhenAuthorizedViaBearerHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/hub/gateway/negotiate?negotiateVersion=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.AdminApiKey);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GatewayNegotiate_ShouldReturn200_WhenAuthorizedViaQueryAccessToken()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/hub/gateway/negotiate?negotiateVersion=1&access_token={_serverOptions.AdminApiKey}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}