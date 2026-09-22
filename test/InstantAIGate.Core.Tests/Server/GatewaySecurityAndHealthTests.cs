using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace InstantAIGate.Core.Tests.Server;

public class GatewaySecurityAndHealthTests : IClassFixture<GatewayTestFixture>
{
    private readonly HttpClient _publicClient;
    private readonly HttpClient _adminClient;

    public GatewaySecurityAndHealthTests(GatewayTestFixture fixture)
    {
        // Client emulating requests to port 5000
        _publicClient = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000")
        });

        // Client emulating requests to port 5001
        _adminClient = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5001")
        });
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin-secret");

        var response = await _publicClient.SendAsync(request);

        // PortRoutingMiddleware should block requests to /admin via port 5000
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicApi_ShouldReturn403_WhenAccessedViaAdminPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "any-tenant-key");

        var response = await _adminClient.SendAsync(request);

        // PortRoutingMiddleware should block requests to /v1 via port 5001
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_ShouldReturn200_WithValidAdminKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin-secret");

        var response = await _adminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_ShouldReturn401_WithInvalidAdminKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret");

        var response = await _adminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}