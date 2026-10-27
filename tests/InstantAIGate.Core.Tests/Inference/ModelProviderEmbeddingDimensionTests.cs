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

public sealed class ModelProviderEmbeddingDimensionTests : IDisposable
{
    private readonly Mock<IBackendFacade> _mockBackendFacade;
    private readonly Mock<IVisionFacade> _mockVisionFacade;
    private readonly Mock<IModelHandle> _mockModelHandle;
    private readonly ModelProvider _modelProvider;

    public ModelProviderEmbeddingDimensionTests()
    {
        _mockBackendFacade = new Mock<IBackendFacade>();
        _mockVisionFacade = new Mock<IVisionFacade>();
        _mockModelHandle = new Mock<IModelHandle>();

        _modelProvider = new ModelProvider(
            NullLogger<ModelProvider>.Instance,
            _mockBackendFacade.Object,
            _mockVisionFacade.Object);
    }

    [Fact]
    public void GetEmbeddingDimension_WhenModelNotLoaded_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _modelProvider.GetEmbeddingDimension("non-existent-model"));

        Assert.Contains("not loaded", ex.Message);
    }

    [Fact]
    public async Task GetEmbeddingDimension_WhenModelLoaded_ReturnsDimensionFromFacade()
    {
        const string repoId = "test-embedding-model";
        const int expectedDimension = 768;

        _mockBackendFacade
            .Setup(b => b.LoadModel(It.IsAny<ModelSettings>(), It.IsAny<string>()))
            .Returns(_mockModelHandle.Object);

        _mockBackendFacade
            .Setup(b => b.GetEmbeddingDimension(_mockModelHandle.Object))
            .Returns(expectedDimension);

        var resolvedPaths = new ResolvedModelPaths("/fake/path.gguf", null, 1000);
        var config = new ModelSettings { RepoId = repoId };

        await _modelProvider.InitializeAsync(config, resolvedPaths);

        int dimension = _modelProvider.GetEmbeddingDimension(repoId);

        Assert.Equal(expectedDimension, dimension);
        _mockBackendFacade.Verify(b => b.GetEmbeddingDimension(_mockModelHandle.Object), Times.Once);
    }

    [Fact]
    public void GetEmbeddingDimension_WithEmptyRepoId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _modelProvider.GetEmbeddingDimension(string.Empty));
    }

    public void Dispose()
    {
        _modelProvider.Dispose();
    }
}