using FluentAssertions;
using InstantAIGate.API.Dtos;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAiGate.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace InstantAIGate.API.Tests.Controllers;

/// <summary>
/// Tests for OpenAiEmbeddingsController - validates OpenAI-compatible embeddings API.
/// SUT: OpenAiEmbeddingsController
/// Mocks: IEmbeddingAdapter (external inference dependency)
/// </summary>
public class OpenAiEmbeddingsControllerTests
{
    private readonly Mock<IEmbeddingAdapter> _embeddingAdapterMock;
    private readonly OpenAiEmbeddingsController _sut;

    public OpenAiEmbeddingsControllerTests()
    {
        _embeddingAdapterMock = new Mock<IEmbeddingAdapter>();
        _sut = new OpenAiEmbeddingsController(_embeddingAdapterMock.Object);
    }

    private OpenAiEmbeddingRequest CreateValidRequest(object input)
    {
        return new OpenAiEmbeddingRequest
        {
            Model = "nomic-embed-text-v1.5",
            Input = input
        };
    }

    private float[] CreateRealisticEmbedding(int dimensions = 768)
    {
        var random = new Random(42); // Fixed seed for deterministic tests
        var embedding = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range: -1 to 1
        }
        return embedding;
    }

    #region Request Validation Tests

    [Fact]
    public async Task CreateEmbedding_NullRequest_ReturnsBadRequest()
    {
        // Arrange
        OpenAiEmbeddingRequest? request = null;
        var ct = CancellationToken.None;

        // Act
        var result = await _sut.CreateEmbedding(request!, ct);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateEmbedding_NullInput_ReturnsBadRequest()
    {
        // Arrange
        var request = new OpenAiEmbeddingRequest
        {
            Model = "nomic-embed-text-v1.5",
            Input = null!
        };
        var ct = CancellationToken.None;

        // Act
        var result = await _sut.CreateEmbedding(request, ct);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("input");
        errorJson.Should().Contain("required");
    }

    [Fact]
    public async Task CreateEmbedding_EmptyModel_ReturnsBadRequest()
    {
        // Arrange
        var request = new OpenAiEmbeddingRequest
        {
            Model = string.Empty,
            Input = "test input"
        };
        var ct = CancellationToken.None;

        // Act
        var result = await _sut.CreateEmbedding(request, ct);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateEmbedding_NullModel_ReturnsBadRequest()
    {
        // Arrange
        var request = new OpenAiEmbeddingRequest
        {
            Model = null!,
            Input = "test input"
        };
        var ct = CancellationToken.None;

        // Act
        var result = await _sut.CreateEmbedding(request, ct);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("model");
    }

    #endregion

    #region Single String Input Tests

    [Fact]
    public async Task CreateEmbedding_SingleStringInput_ReturnsOkWithEmbedding()
    {
        // Arrange
        var inputText = "The quick brown fox jumps over the lazy dog";
        var request = CreateValidRequest(inputText);
        var expectedEmbedding = CreateRealisticEmbedding(768);

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedEmbedding });

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OpenAiEmbeddingResponse>().Subject;

        response.data.Should().HaveCount(1);
        response.data[0].Embedding.Should().HaveCount(768);
        response.data[0].Index.Should().Be(0);
    }

    [Fact]
    public async Task CreateEmbedding_SingleString_CallsAdapterWithCorrectParameters()
    {
        // Arrange
        var inputText = "Embeddings are vector representations of text";
        var modelName = "nomic-embed-text-v1.5";
        var requestWithModel = new OpenAiEmbeddingRequest
        {
            Model = modelName,
            Input = inputText
        };

        List<string>? capturedInput = null;
        string? capturedModel = null;

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, CancellationToken>((model, input, _) =>
            {
                capturedModel = model;
                capturedInput = input;
            })
            .ReturnsAsync(new[] { CreateRealisticEmbedding() });

        // Act
        await _sut.CreateEmbedding(requestWithModel, CancellationToken.None);

        // Assert
        capturedModel.Should().Be(modelName);
        capturedInput.Should().NotBeNull();
        capturedInput!.Should().HaveCount(1);
        capturedInput[0].Should().Be(inputText);
    }

    #endregion

    #region Array Input Tests

    [Fact]
    public async Task CreateEmbedding_ArrayInput_ReturnsMultipleEmbeddings()
    {
        // Arrange
        var inputTexts = new[]
        {
            "Natural language processing",
            "Machine learning models",
            "Deep neural networks"
        };

        var inputJson = System.Text.Json.JsonSerializer.SerializeToElement(inputTexts);
        var request = CreateValidRequest(inputJson);

        var embeddings = new[]
        {
            CreateRealisticEmbedding(768),
            CreateRealisticEmbedding(768),
            CreateRealisticEmbedding(768)
        };

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OpenAiEmbeddingResponse>().Subject;

        response.data.Should().HaveCount(3);
        response.data[0].Index.Should().Be(0);
        response.data[1].Index.Should().Be(1);
        response.data[2].Index.Should().Be(2);
    }

    [Fact]
    public async Task CreateEmbedding_BatchInput_PreservesOrder()
    {
        // Arrange
        var inputTexts = new[]
        {
            "First document",
            "Second document",
            "Third document",
            "Fourth document"
        };

        var inputJson = System.Text.Json.JsonSerializer.SerializeToElement(inputTexts);
        var request = CreateValidRequest(inputJson);

        var embeddings = Enumerable.Range(0, 4)
            .Select(_ => CreateRealisticEmbedding(768))
            .ToArray();

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiEmbeddingResponse;

        for (int i = 0; i < 4; i++)
        {
            response!.data[i].Index.Should().Be(i, $"index {i} should be preserved");
        }
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public async Task CreateEmbedding_Response_ContainsRequiredFields()
    {
        // Arrange
        var request = CreateValidRequest("test input");

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateRealisticEmbedding() });

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiEmbeddingResponse;

        response.Should().NotBeNull();
        response!.model.Should().NotBeNullOrEmpty();
        response.data.Should().NotBeNull();
        response.usage.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateEmbedding_Response_ModelMatchesRequest()
    {
        // Arrange
        var modelName = "bge-large-en-v1.5";
        var request = new OpenAiEmbeddingRequest
        {
            Model = modelName,
            Input = "semantic search query"
        };

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateRealisticEmbedding() });

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiEmbeddingResponse;
        response!.model.Should().Be(modelName);
    }

    [Fact]
    public async Task CreateEmbedding_Response_EmbeddingDataHasCorrectStructure()
    {
        // Arrange
        var request = CreateValidRequest("vector database query");
        var embedding = CreateRealisticEmbedding(1024);

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { embedding });

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiEmbeddingResponse;

        var embeddingData = response!.data[0];
        embeddingData.Embedding.Should().NotBeNull();
        embeddingData.Embedding.Should().HaveCount(1024);
        embeddingData.Index.Should().Be(0);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CreateEmbedding_ModelNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new OpenAiEmbeddingRequest
        {
            Model = "non-existent-model",
            Input = "test input"
        };

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Model 'non-existent-model' not registered"));

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        var notFound = result as NotFoundObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(notFound!.Value);
        errorJson.Should().Contain("ModelNotRegistered");
    }

    [Fact]
    public async Task CreateEmbedding_InvalidOperation_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest("test input");
        var exceptionMessage = "Model is not an embedding model";

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("ModelExecutionError");
        errorJson.Should().Contain(exceptionMessage);
    }

    [Fact]
    public async Task CreateEmbedding_UnexpectedException_Returns500ServerError()
    {
        // Arrange
        var request = CreateValidRequest("test input");

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected GPU error"));

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task CreateEmbedding_CancellationRequested_ReturnsServerError()
    {
        // Arrange
        var request = CreateValidRequest("test input");

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region Realistic Data Tests

    [Fact]
    public async Task CreateEmbedding_SemanticSearchScenario_MultipleQueries()
    {
        // Arrange
        var queries = new[]
        {
            "How to implement vector search?",
            "Best practices for embedding models",
            "Semantic similarity computation techniques"
        };

        var inputJson = System.Text.Json.JsonSerializer.SerializeToElement(queries);
        var request = new OpenAiEmbeddingRequest
        {
            Model = "all-MiniLM-L6-v2",
            Input = inputJson
        };

        var embeddings = queries
            .Select(_ => CreateRealisticEmbedding(384))
            .ToArray();

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as OpenAiEmbeddingResponse;

        response!.data.Should().HaveCount(3);
        response.data.All(d => d.Embedding.Count == 384).Should().BeTrue();
    }

    [Fact]
    public async Task CreateEmbedding_LongDocument_HandlesExtendedText()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat(
            "This is a sentence that will be repeated to create a long document for testing purposes.", 
            50));
        var request = new OpenAiEmbeddingRequest
        {
            Model = "nomic-embed-text-v1.5",
            Input = longText
        };

        _embeddingAdapterMock
            .Setup(a => a.GetEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateRealisticEmbedding(768) });

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateEmbedding_EmptyArrayInput_ReturnsBadRequest()
    {
        // Arrange
        var emptyArray = System.Text.Json.JsonSerializer.SerializeToElement(new string[0]);
        var request = CreateValidRequest(emptyArray);

        // Act
        var result = await _sut.CreateEmbedding(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        var errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest!.Value);
        errorJson.Should().Contain("empty");
    }

    #endregion
}
