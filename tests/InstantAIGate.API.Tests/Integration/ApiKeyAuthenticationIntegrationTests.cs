using FluentAssertions;
using InstantAIGate.Application.Config;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace InstantAIGate.API.Tests.Integration;

/// <summary>
/// Integration tests for API key authentication with real HTTP requests.
/// Tests the full authentication pipeline including middleware and authorization.
/// </summary>
public class ApiKeyAuthenticationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestApiKey = "test-integration-key-12345";
    private const string TestEndpoint = "/api/admin/models"; // Admin endpoint requires authentication

    public ApiKeyAuthenticationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override GatewayConfig for testing
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(GatewayConfig));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton(new GatewayConfig
                {
                    AdminKey = TestApiKey
                });
            });
        });
    }

    #region Header-Based Authentication Integration Tests

    [Fact]
    public async Task GetModels_WithValidApiKeyInHeader_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithInvalidApiKeyInHeader_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unauthorized");
        content.Should().Contain("Invalid or missing API Key");
    }

    [Fact]
    public async Task GetModels_WithoutApiKey_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithEmptyApiKeyHeader_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", string.Empty);

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Query String Authentication Integration Tests

    [Fact]
    public async Task GetModels_WithValidApiKeyInQueryString_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"{TestEndpoint}?apiKey={TestApiKey}");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithInvalidApiKeyInQueryString_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"{TestEndpoint}?apiKey=wrong-key");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithEmptyApiKeyInQueryString_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"{TestEndpoint}?apiKey=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Priority and Mixed Tests

    [Fact]
    public async Task GetModels_WithValidHeaderAndInvalidQuery_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);

        // Act
        var response = await client.GetAsync($"{TestEndpoint}?apiKey=wrong-key");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithInvalidHeaderAndValidQuery_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        // Act
        var response = await client.GetAsync($"{TestEndpoint}?apiKey={TestApiKey}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public async Task GetModels_Unauthorized_ReturnsJsonResponse()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        json.Should().NotBeNull();
        json.Should().ContainKey("error");
        json.Should().ContainKey("message");
    }

    [Fact]
    public async Task GetModels_UnauthorizedWithInvalidKey_ContainsErrorMessage()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "invalid");

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        json.Should().NotBeNull();
        json!["error"].ToString().Should().Be("Unauthorized");
        json["message"].ToString().Should().Contain("X-Api-Key");
        json["message"].ToString().Should().Contain("apiKey");
    }

    #endregion

    #region Special Characters Tests

    [Fact]
    public async Task GetModels_WithSpecialCharactersInApiKey_WorksCorrectly()
    {
        // Arrange
        const string specialKey = "key-@#$%_test!";
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(GatewayConfig));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton(new GatewayConfig { AdminKey = specialKey });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", specialKey);

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Skip Mode Tests

    [Fact]
    public async Task GetModels_WithAdminKeySkip_AllowsAccessWithoutKey()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(GatewayConfig));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton(new GatewayConfig { AdminKey = "skip" });
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WithAdminKeyEmpty_AllowsAccessWithoutKey()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(GatewayConfig));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton(new GatewayConfig { AdminKey = string.Empty });
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public async Task GetModels_ApiKeyIsCaseSensitive_Returns401ForWrongCase()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey.ToUpper());

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_HeaderNameIsCaseInsensitive_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", TestApiKey);

        // Act
        var response = await client.GetAsync(TestEndpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    #endregion
}
