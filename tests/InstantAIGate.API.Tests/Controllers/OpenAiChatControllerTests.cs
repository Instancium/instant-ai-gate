using FluentAssertions;
using InstantAIGate.API.Controllers;
using InstantAIGate.API.Dtos;
using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Inference;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace InstantAIGate.API.Tests.Controllers;

/// <summary>
/// Tests for OpenAiChatController - validates OpenAI-compatible chat completion API.
/// SUT: OpenAiChatController
/// Mocks: IChatAdapter (external inference dependency)
/// </summary>
public class OpenAiChatControllerTests
{
    private readonly Mock<IChatAdapter> _chatAdapterMock;
    private readonly OpenAiChatController _sut;

    public OpenAiChatControllerTests()
    {
        _chatAdapterMock = new Mock<IChatAdapter>();
        _sut = new OpenAiChatController(_chatAdapterMock.Object);

        // Setup HttpContext for controller (required for streaming tests)
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private OpenAiChatRequest CreateValidRequest(bool stream = false)
    {
        return new OpenAiChatRequest
        {
            Model = "llama-3.1-8b-instruct",
            Messages = new List<OpenAiMessage>
            {
                new() { Role = "system", Content = "You are a helpful AI assistant." },
                new() { Role = "user", Content = "What is the capital of France?" }
            },
            Temperature = 0.7f,
            MaxTokens = 150,
            Stream = stream
        };
    }

    #region Request Validation Tests

    [Fact]
    public async Task CreateChatCompletion_NullRequest_ReturnsBadRequest()
    {
        // Arrange
        OpenAiChatRequest? request = null;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        badRequest!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateChatCompletion_EmptyModel_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Model = string.Empty;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateChatCompletion_NullModel_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Model = null;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("model");
        errorJson.Should().Contain("required");
    }

    [Fact]
    public async Task CreateChatCompletion_NullMessages_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Messages = null;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("messages");
    }

    [Fact]
    public async Task CreateChatCompletion_EmptyMessages_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Messages = new List<OpenAiMessage>();

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(-1f)]
    [InlineData(2.1f)]
    [InlineData(5f)]
    public async Task CreateChatCompletion_InvalidTemperature_ReturnsBadRequest(float invalidTemperature)
    {
        // Arrange
        var request = CreateValidRequest();
        request.Temperature = invalidTemperature;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("Temperature");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public async Task CreateChatCompletion_ValidTemperature_AcceptsRequest(float validTemperature)
    {
        // Arrange
        var request = CreateValidRequest();
        request.Temperature = validTemperature;

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Paris is the capital of France.");

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(2f)]
    public async Task CreateChatCompletion_InvalidTopP_ReturnsBadRequest(float invalidTopP)
    {
        // Arrange
        var request = CreateValidRequest();
        request.TopP = invalidTopP;

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("Top_p");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    [InlineData(1.0f)]
    public async Task CreateChatCompletion_ValidTopP_AcceptsRequest(float validTopP)
    {
        // Arrange
        var request = CreateValidRequest();
        request.TopP = validTopP;

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Paris is the capital of France.");

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region Non-Streaming Tests

    [Fact]
    public async Task CreateChatCompletion_ValidRequest_ReturnsOkWithResponse()
    {
        // Arrange
        var request = CreateValidRequest(stream: false);
        var expectedContent = "Paris is the capital of France.";

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContent);

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();

        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        responseJson.Should().Contain(expectedContent);
        responseJson.Should().Contain(request.Model!);
    }

    [Fact]
    public async Task CreateChatCompletion_NonStreaming_CallsGenerateAsync()
    {
        // Arrange
        var request = CreateValidRequest(stream: false);

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Response content");

        // Act
        await _sut.CreateChatCompletion(request);

        // Assert
        _chatAdapterMock.Verify(
            a => a.GenerateAsync(
                It.Is<LlamaChatRequest>(r => r.Messages.Count == request.Messages!.Count),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateChatCompletion_NonStreaming_MapsRequestParametersCorrectly()
    {
        // Arrange
        var request = CreateValidRequest(stream: false);
        request.Temperature = 0.8f;
        request.TopP = 0.95f;
        request.MaxTokens = 200;

        LlamaChatRequest? capturedRequest = null;
        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LlamaChatRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync("Response");

        // Act
        await _sut.CreateChatCompletion(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Temperature.Should().Be(0.8f);
        capturedRequest.TopP.Should().Be(0.95f);
        capturedRequest.MaxTokens.Should().Be(200);
        capturedRequest.Model.Should().Be("llama-3.1-8b-instruct");
    }

    [Fact]
    public async Task CreateChatCompletion_MultipleMessages_PreservesConversationHistory()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Messages = new List<OpenAiMessage>
        {
            new() { Role = "system", Content = "You are a helpful assistant." },
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi! How can I help you?" },
            new() { Role = "user", Content = "Tell me about AI" }
        };

        LlamaChatRequest? capturedRequest = null;
        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LlamaChatRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync("AI is artificial intelligence.");

        // Act
        await _sut.CreateChatCompletion(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(4);
        capturedRequest.Messages[0].Role.Should().Be("system");
        capturedRequest.Messages[1].Role.Should().Be("user");
        capturedRequest.Messages[2].Role.Should().Be("assistant");
        capturedRequest.Messages[3].Role.Should().Be("user");
    }

    [Fact]
    public async Task CreateChatCompletion_AdapterThrowsInvalidOperationException_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        var exceptionMessage = "Model 'llama-3.1-8b-instruct' not found";

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain(exceptionMessage.Replace("'", "\\u0027"));
    }

    [Fact]
    public async Task CreateChatCompletion_OperationCanceled_Returns499ClientClosedRequest()
    {
        // Arrange
        var request = CreateValidRequest();

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(499);
    }

    [Fact]
    public async Task CreateChatCompletion_UnexpectedException_Returns500ServerError()
    {
        // Arrange
        var request = CreateValidRequest();

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public async Task CreateChatCompletion_Response_ContainsRequiredFields()
    {
        // Arrange
        var request = CreateValidRequest();

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test response");

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();

        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        responseJson.Should().Contain("\"id\"");
        responseJson.Should().Contain("\"object\":\"chat.completion\"");
        responseJson.Should().Contain("\"created\"");
        responseJson.Should().Contain("\"model\":\""+request.Model);
        responseJson.Should().Contain("\"choices\"");
        responseJson.Should().Contain("\"usage\"");
    }

    [Fact]
    public async Task CreateChatCompletion_Response_IdStartsWithChatcmpl()
    {
        // Arrange
        var request = CreateValidRequest();

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test response");

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        var okResult = result as OkObjectResult;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult!.Value);
        responseJson.Should().Contain("\"id\":\"chatcmpl-");
    }

    [Fact]
    public async Task CreateChatCompletion_Response_ChoiceHasCorrectStructure()
    {
        // Arrange
        var request = CreateValidRequest();
        var expectedContent = "This is the AI response";

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContent);

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        var okResult = result as OkObjectResult;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult!.Value);

        responseJson.Should().Contain("\"index\":0");
        responseJson.Should().Contain("\"role\":\"assistant\"");
        responseJson.Should().Contain(expectedContent);
        responseJson.Should().Contain("\"finish_reason\":\"stop\"");
    }

    #endregion

    #region Realistic Data Tests

    [Fact]
    public async Task CreateChatCompletion_CodeGenerationScenario_HandlesMultilineResponse()
    {
        // Arrange
        var request = new OpenAiChatRequest
        {
            Model = "llama-3.1-70b-instruct",
            Messages = new List<OpenAiMessage>
            {
                new() { Role = "system", Content = "You are an expert programmer." },
                new() { Role = "user", Content = "Write a Python function to calculate factorial." }
            },
            Temperature = 0.3f,
            MaxTokens = 300
        };

        var codeResponse = @"Here's a factorial function:

```python
def factorial(n):
    if n == 0 or n == 1:
        return 1
    return n * factorial(n - 1)
```";

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeResponse);

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        var okResult = result as OkObjectResult;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult!.Value);
        responseJson.Should().Contain("factorial");
        responseJson.Should().Contain("\\u0060\\u0060\\u0060python"); // JSON-escaped backticks
    }

    [Fact]
    public async Task CreateChatCompletion_EmptyAdapterResponse_ReturnsEmptyContent()
    {
        // Arrange
        var request = CreateValidRequest();

        _chatAdapterMock
            .Setup(a => a.GenerateAsync(It.IsAny<LlamaChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        // Act
        var result = await _sut.CreateChatCompletion(request);

        // Assert
        var okResult = result as OkObjectResult;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(okResult!.Value);
        responseJson.Should().Contain("\"content\":\"\"");
    }

    #endregion
}
