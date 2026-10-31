namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Tests.TestConfiguration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
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

    [Fact]
    public async Task Negotiate_WithoutToken_Returns401()
    {
        using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/hub/gateway/negotiate?negotiateVersion=1", body);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RootRoute_IsNotBlockedByApiKeyMiddleware()
    {
        var response = await _client.GetAsync("/");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RootRoute_Returns200Ok_WithHtmlDashboardEntryPoint()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.MediaType ?? string.Empty);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InstantAIGate", body);
        Assert.Contains("blazor.webassembly.js", body);
    }

    [Fact]
    public async Task IndexHtml_Returns200Ok_WithHtmlContentType()
    {
        var response = await _client.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.MediaType ?? string.Empty);
    }

    [Fact]
    public async Task StaticAssetPaths_WithoutToken_AreNotRejectedWith401()
    {
        foreach (string path in new[] { "/index.html", "/css/app.css", "/_framework/blazor.webassembly.js", "/favicon.ico" })
        {
            var response = await _client.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}