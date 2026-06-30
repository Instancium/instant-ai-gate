using FluentAssertions;
using InstantAIGate.API.Dtos;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAiGate.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace InstantAIGate.API.Tests.Controllers;

/// <summary>
/// Tests for OpenAiModelsController - validates OpenAI-compatible models list API.
/// SUT: OpenAiModelsController
/// Mocks: IModelManager (external model management dependency)
/// </summary>
public class OpenAiModelsControllerTests
{
    private readonly Mock<IModelManager> _modelManagerMock;
    private readonly OpenAiModelsController _sut;

    public OpenAiModelsControllerTests()
    {
        _modelManagerMock = new Mock<IModelManager>();
        _sut = new OpenAiModelsController(_modelManagerMock.Object);
    }

    #region Successful Response Tests

    [Fact]
    public void GetOpenAiModels_WithActiveModels_ReturnsOkWithModelList()
    {
        // Arrange
        var activeModels = new List<string>
        {
            "llama-3.1-8b-instruct",
            "mistral-7b-v0.3",
            "qwen2.5-14b-instruct"
        };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(activeModels);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OpenAiModelListResponse>().Subject;

        response.data.Should().HaveCount(3);
        response.data.Select(m => m.id).Should().Contain(activeModels);
    }

    [Fact]
    public void GetOpenAiModels_NoActiveModels_ReturnsEmptyList()
    {
        // Arrange
        var emptyList = new List<string>();

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(emptyList);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OpenAiModelListResponse>().Subject;

        response.data.Should().BeEmpty();
    }

    [Fact]
    public void GetOpenAiModels_NullFromManager_ReturnsEmptyList()
    {
        // Arrange
        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns((List<string>)null!);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OpenAiModelListResponse>().Subject;

        response.data.Should().NotBeNull();
        response.data.Should().BeEmpty();
    }

    [Fact]
    public void GetOpenAiModels_CallsGetActiveModels()
    {
        // Arrange
        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(new List<string>());

        // Act
        _sut.GetOpenAiModels();

        // Assert
        _modelManagerMock.Verify(m => m.GetActiveModels(), Times.Once);
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public void GetOpenAiModels_Response_HasCorrectStructure()
    {
        // Arrange
        var activeModels = new List<string> { "phi-3-mini-4k-instruct" };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(activeModels);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response.Should().NotBeNull();
        response!.data.Should().NotBeNull();
        response.data.Should().HaveCount(1);
        response.data[0].id.Should().Be("phi-3-mini-4k-instruct");
    }

    [Fact]
    public void GetOpenAiModels_MultipleModels_PreservesAllIds()
    {
        // Arrange
        var activeModels = new List<string>
        {
            "llama-3.2-1b-instruct",
            "gemma-2-9b-it",
            "deepseek-coder-6.7b-instruct",
            "starcoder2-15b"
        };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(activeModels);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().HaveCount(4);
        foreach (var modelId in activeModels)
        {
            response.data.Should().Contain(m => m.id == modelId, 
                $"response should include model '{modelId}'");
        }
    }

    #endregion

    #region Data Filtering Tests

    [Fact]
    public void GetOpenAiModels_WhitespaceModelIds_AreFiltered()
    {
        // Arrange
        var modelsWithWhitespace = new List<string>
        {
            "llama-3.1-8b-instruct",
            "",
            "mistral-7b-v0.3",
            "   ",
            "qwen2.5-14b-instruct"
        };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(modelsWithWhitespace);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().HaveCount(3);
        response.data.Should().NotContain(m => string.IsNullOrWhiteSpace(m.id));
    }

    [Fact]
    public void GetOpenAiModels_OnlyWhitespaceIds_ReturnsEmptyList()
    {
        // Arrange
        var whitespaceOnly = new List<string> { "", "   ", "\t", "\n" };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(whitespaceOnly);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().BeEmpty();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GetOpenAiModels_ManagerThrowsException_Returns500ServerError()
    {
        // Arrange
        var exceptionMessage = "Failed to retrieve active models from registry";

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Throws(new Exception(exceptionMessage));

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);

        var errorJson = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        errorJson.Should().Contain("ModelListRetrievalError");
        errorJson.Should().Contain(exceptionMessage);
    }

    [Fact]
    public void GetOpenAiModels_InvalidOperationException_Returns500()
    {
        // Arrange
        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Throws(new InvalidOperationException("Model manager not initialized"));

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region Realistic Data Tests

    [Fact]
    public void GetOpenAiModels_RealisticProductionScenario_MultipleLoadedModels()
    {
        // Arrange
        var productionModels = new List<string>
        {
            "llama-3.3-70b-instruct",
            "mistral-nemo-instruct-2407",
            "qwen2.5-32b-instruct",
            "deepseek-r1-distill-llama-70b",
            "phi-4",
            "nomic-embed-text-v1.5"
        };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(productionModels);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().HaveCount(6);
        response.data.Should().Contain(m => m.id.Contains("llama"));
        response.data.Should().Contain(m => m.id.Contains("mistral"));
        response.data.Should().Contain(m => m.id.Contains("qwen"));
        response.data.Should().Contain(m => m.id.Contains("embed"));
    }

    [Fact]
    public void GetOpenAiModels_SingleModelLoaded_ReturnsOneModel()
    {
        // Arrange
        var singleModel = new List<string> { "granite-3.1-8b-instruct" };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(singleModel);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().ContainSingle();
        response.data[0].id.Should().Be("granite-3.1-8b-instruct");
    }

    [Fact]
    public void GetOpenAiModels_ModelIdsWithSpecialCharacters_PreservesFormat()
    {
        // Arrange
        var modelsWithSpecialChars = new List<string>
        {
            "llama-3.1-8b-instruct-q4_k_m",
            "mistral-7b-v0.3-GGUF",
            "qwen2.5_14b_instruct"
        };

        _modelManagerMock
            .Setup(m => m.GetActiveModels())
            .Returns(modelsWithSpecialChars);

        // Act
        var result = _sut.GetOpenAiModels();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiModelListResponse;

        response!.data.Should().HaveCount(3);
        response.data[0].id.Should().Contain("q4_k_m");
        response.data[1].id.Should().Contain("GGUF");
        response.data[2].id.Should().Contain("_");
    }

    #endregion
}
