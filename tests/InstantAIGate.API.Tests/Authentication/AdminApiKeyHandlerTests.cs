using FluentAssertions;
using InstantAIGate.API.Authentication;
using InstantAIGate.Application.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace InstantAIGate.API.Tests.Authentication;

/// <summary>
/// Tests for AdminApiKeyHandler - validates API key authentication logic.
/// SUT: AdminApiKeyHandler
/// Tests cover: header-based auth, query string auth, skip mode, invalid keys, missing keys
/// </summary>
public class AdminApiKeyHandlerTests
{
    private readonly Mock<IOptionsMonitor<AuthenticationSchemeOptions>> _schemeOptionsMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<AdminApiKeyHandler>> _loggerMock;
    private readonly UrlEncoder _urlEncoder;
    private readonly GatewayConfig _gatewayConfig;
    private readonly AuthenticationScheme _scheme;

    public AdminApiKeyHandlerTests()
    {
        _schemeOptionsMock = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerMock = new Mock<ILogger<AdminApiKeyHandler>>();
        _urlEncoder = UrlEncoder.Default;
        _gatewayConfig = new GatewayConfig();
        _scheme = new AuthenticationScheme("TestScheme", "Test Scheme", typeof(AdminApiKeyHandler));

        _schemeOptionsMock.Setup(x => x.Get(It.IsAny<string>()))
            .Returns(new AuthenticationSchemeOptions());
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);
    }

    private AdminApiKeyHandler CreateHandler(HttpContext httpContext)
    {
        var handler = new AdminApiKeyHandler(
            _schemeOptionsMock.Object,
            _loggerFactoryMock.Object,
            _urlEncoder,
            _gatewayConfig);

        handler.InitializeAsync(_scheme, httpContext).Wait();
        return handler;
    }

    #region Skip Authentication Mode Tests

    [Fact]
    public async Task HandleAuthenticateAsync_AdminKeyEmpty_ReturnsSuccessWithSkipAuth()
    {
        // Arrange
        _gatewayConfig.AdminKey = string.Empty;
        var context = new DefaultHttpContext();
        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity.Should().NotBeNull();
        result.Principal.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "SkipAuth");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AdminKeySkip_ReturnsSuccessWithSkipAuth()
    {
        // Arrange
        _gatewayConfig.AdminKey = "skip";
        var context = new DefaultHttpContext();
        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "SkipAuth");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AdminKeySkipCaseInsensitive_ReturnsSuccessWithSkipAuth()
    {
        // Arrange
        _gatewayConfig.AdminKey = "SKIP";
        var context = new DefaultHttpContext();
        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AdminKeyWhitespace_ReturnsSuccessWithSkipAuth()
    {
        // Arrange
        _gatewayConfig.AdminKey = "   ";
        var context = new DefaultHttpContext();
        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal!.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "SkipAuth");
    }

    #endregion

    #region Header-Based Authentication Tests

    [Fact]
    public async Task HandleAuthenticateAsync_ValidApiKeyInHeader_ReturnsSuccess()
    {
        // Arrange
        const string validApiKey = "test-admin-key-12345";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = validApiKey;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity.Should().NotBeNull();
        result.Principal.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.Identity.AuthenticationType.Should().Be("TestScheme");
        result.Principal.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "Admin");
        result.Principal.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_InvalidApiKeyInHeader_ReturnsFailure()
    {
        // Arrange
        _gatewayConfig.AdminKey = "correct-key";

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "wrong-key";

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ApiKeyCaseSensitive_ReturnsFailure()
    {
        // Arrange
        _gatewayConfig.AdminKey = "TestKey";

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "testkey";

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_EmptyApiKeyInHeader_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = string.Empty;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeNull();
        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_WhitespaceApiKeyInHeader_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "   ";

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.None.Should().BeTrue();
    }

    #endregion

    #region Query String Authentication Tests

    [Fact]
    public async Task HandleAuthenticateAsync_ValidApiKeyInQueryString_ReturnsSuccess()
    {
        // Arrange
        const string validApiKey = "query-test-key";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?apiKey={validApiKey}");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal!.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "Admin");
        result.Principal.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_InvalidApiKeyInQueryString_ReturnsFailure()
    {
        // Arrange
        _gatewayConfig.AdminKey = "correct-key";

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=wrong-key");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_EmptyApiKeyInQueryString_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.None.Should().BeTrue();
    }

    #endregion

    #region Priority Tests - Header vs Query String

    [Fact]
    public async Task HandleAuthenticateAsync_ValidKeyInHeaderAndQuery_PrefersHeader()
    {
        // Arrange
        const string validApiKey = "header-key";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = validApiKey;
        context.Request.QueryString = new QueryString("?apiKey=query-key");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_InvalidHeaderValidQuery_FailsWithHeaderKey()
    {
        // Arrange
        const string validApiKey = "correct-key";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "wrong-header-key";
        context.Request.QueryString = new QueryString($"?apiKey={validApiKey}");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_EmptyHeaderValidQuery_ReturnsNoResult()
    {
        // Arrange
        const string validApiKey = "query-key";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = string.Empty;
        context.Request.QueryString = new QueryString($"?apiKey={validApiKey}");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert - Header takes priority even if empty, resulting in NoResult due to whitespace check
        result.None.Should().BeTrue();
    }

    #endregion

    #region Missing API Key Tests

    [Fact]
    public async Task HandleAuthenticateAsync_NoHeaderNoQuery_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeNull();
        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_OnlyOtherQueryParams_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?param1=value1&param2=value2");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_OnlyOtherHeaders_ReturnsNoResult()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer token";
        context.Request.Headers["Content-Type"] = "application/json";

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.None.Should().BeTrue();
    }

    #endregion

    #region Special Characters and Edge Cases

    [Fact]
    public async Task HandleAuthenticateAsync_ApiKeyWithSpecialCharacters_ValidatesCorrectly()
    {
        // Arrange
        const string specialKey = "key-with-@#$%_special!chars";
        _gatewayConfig.AdminKey = specialKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = specialKey;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_VeryLongApiKey_ValidatesCorrectly()
    {
        // Arrange
        var longKey = new string('a', 500);
        _gatewayConfig.AdminKey = longKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = longKey;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_UnicodeApiKey_ValidatesCorrectly()
    {
        // Arrange
        const string unicodeKey = "ключ-тест-🔑-キー";
        _gatewayConfig.AdminKey = unicodeKey;

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = unicodeKey;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ApiKeyWithLeadingTrailingSpaces_MatchesExact()
    {
        // Arrange
        const string keyWithSpaces = "  key-with-spaces  ";
        _gatewayConfig.AdminKey = "key-without-spaces";

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = keyWithSpaces;

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid API Key");
    }

    #endregion

    #region Authentication Challenge Tests

    [Fact]
    public async Task HandleChallengeAsync_Returns401WithJsonMessage()
    {
        // Arrange
        _gatewayConfig.AdminKey = "valid-key";

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handler = CreateHandler(context);

        // Act - Trigger authentication and challenge
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        // Assert
        context.Response.StatusCode.Should().Be(401);
        context.Response.ContentType.Should().Be("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        responseBody.Should().Contain("error");
        responseBody.Should().Contain("Unauthorized");
        responseBody.Should().Contain("X-Api-Key");
        responseBody.Should().Contain("apiKey");
    }

    #endregion

    #region Multiple Header Values Tests

    [Fact]
    public async Task HandleAuthenticateAsync_MultipleApiKeyHeaderValues_ConcatenatesValues()
    {
        // Arrange
        const string validApiKey = "first-key,second-key";
        _gatewayConfig.AdminKey = validApiKey;

        var context = new DefaultHttpContext();
        context.Request.Headers.Append("X-Api-Key", "first-key");
        context.Request.Headers.Append("X-Api-Key", "second-key");

        var handler = CreateHandler(context);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert - StringValues concatenates multiple values with comma
        result.Succeeded.Should().BeTrue();
    }

    #endregion
}
