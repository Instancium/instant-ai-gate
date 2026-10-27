namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class ModelManagerEmbeddingDimensionTests : IDisposable
{
    private readonly Mock<IModelProvider> _mockModelProvider;
    private readonly Mock<IModelLocator> _mockModelLocator;
    private readonly Mock<IQueueManager> _mockQueueManager;
    private readonly ModelManager _modelManager;

    public ModelManagerEmbeddingDimensionTests()
    {
        _mockModelProvider = new Mock<IModelProvider>();
        _mockModelLocator = new Mock<IModelLocator>();
        _mockQueueManager = new Mock<IQueueManager>();

        _modelManager = new ModelManager(
            _mockModelProvider.Object,
            _mockModelLocator.Object,
            _mockQueueManager.Object,
            NullLogger<ModelManager>.Instance);
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_WhenNoActiveModel_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _modelManager.GetEmbeddingDimensionAsync("any-model"));

        Assert.Contains("No model is currently active", ex.Message);
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_WhenModelNotActive_ThrowsInvalidOperationException()
    {
        // Arrange: загружаем модель "active-model", чтобы _activeConfig != null
        var activeConfig = new ModelSettings { RepoId = "active-model" };
        var resolvedPaths = new ResolvedModelPaths("/fake/path.gguf", null, 1000);

        _mockModelProvider
            .Setup(p => p.InitializeAsync(
                It.IsAny<ModelSettings>(),
                It.IsAny<ResolvedModelPaths>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _modelManager.LoadModelAsync(activeConfig);

        // Act & Assert: запрашиваем размерность для ДРУГОЙ модели — должна сработать
        // вторая ветка guard-а: "Model '{repoId}' is not active. Active model: 'active-model'."
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _modelManager.GetEmbeddingDimensionAsync("different-model"));

        Assert.Contains("is not active", ex.Message);
        Assert.Contains("active-model", ex.Message);
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_WhenModelActive_ReturnsDimension()
    {
        // Arrange
        const string repoId = "test-embedding-model";
        const int expectedDimension = 768;

        var config = new ModelSettings { RepoId = repoId };
        var resolvedPaths = new ResolvedModelPaths("/fake/path.gguf", null, 1000);

        _mockModelProvider
            .Setup(p => p.InitializeAsync(
                It.IsAny<ModelSettings>(),
                It.IsAny<ResolvedModelPaths>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockModelProvider
            .Setup(p => p.GetEmbeddingDimension(repoId))
            .Returns(expectedDimension);

        await _modelManager.LoadModelAsync(config);

        // Act
        int dimension = await _modelManager.GetEmbeddingDimensionAsync(repoId);

        // Assert
        Assert.Equal(expectedDimension, dimension);
        _mockModelProvider.Verify(
            p => p.GetEmbeddingDimension(repoId),
            Times.Once);
    }

    public void Dispose()
    {
        _modelManager.Dispose();
    }
}