namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class MultiModelManagerTests : IDisposable
{
    private readonly Mock<IModelProvider> _mockModelProvider;
    private readonly Mock<IModelLocator> _mockModelLocator;
    private readonly Mock<IQueueManager> _mockQueueManager;
    private readonly ModelManager _modelManager;

    public MultiModelManagerTests()
    {
        _mockModelProvider = new Mock<IModelProvider>();
        _mockModelLocator = new Mock<IModelLocator>();
        _mockQueueManager = new Mock<IQueueManager>();

        _mockModelLocator
            .Setup(l => l.ResolvePathsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string repoId, bool _, CancellationToken _) =>
                new ResolvedModelPaths($"/models/{repoId}.gguf", null, 1024));

        _mockModelProvider
            .Setup(p => p.InitializeAsync(It.IsAny<ModelSettings>(), It.IsAny<ResolvedModelPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _modelManager = new ModelManager(
            _mockModelProvider.Object,
            _mockModelLocator.Object,
            _mockQueueManager.Object,
            NullLogger<ModelManager>.Instance);
    }

    [Fact]
    public async Task LoadModelAsync_WithTwoModels_KeepsBothModelsActiveInPool()
    {
        var vlmConfig = new ModelSettings
        {
            RepoId = "qwen3-vl-8b-instruct",
            Type = ModelType.Vlm,
            VisionSupport = true,
            Embeddings = false,
            MaxContexts = 2
        };

        var embedConfig = new ModelSettings
        {
            RepoId = "qwen3-embedding-0.6b",
            Type = ModelType.Embedding,
            Embeddings = true,
            MaxContexts = 4
        };

        await _modelManager.LoadModelAsync(vlmConfig);
        await _modelManager.LoadModelAsync(embedConfig);

        var activeModels = _modelManager.GetActiveModels().ToList();

        Assert.Equal(2, activeModels.Count);
        Assert.Contains("qwen3-vl-8b-instruct", activeModels);
        Assert.Contains("qwen3-embedding-0.6b", activeModels);

        Assert.NotNull(_modelManager.GetModelSettings("qwen3-vl-8b-instruct"));
        Assert.NotNull(_modelManager.GetModelSettings("qwen3-embedding-0.6b"));
        Assert.True(_modelManager.GetModelSettings("qwen3-embedding-0.6b")!.Embeddings);

        _mockModelProvider.Verify(p => p.UnloadModel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AcquireContextAsync_EnforcesPerModelMaxContextsIndependently()
    {
        var modelA = new ModelSettings { RepoId = "model-a", MaxContexts = 1 };
        var modelB = new ModelSettings { RepoId = "model-b", MaxContexts = 1 };

        _mockModelProvider
            .Setup(p => p.GetInferenceContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var handle = new Mock<IContextHandle>().Object;
                var textCtx = new ModelContext(handle, (_, _) => { });
                return new InferenceContext(textCtx, null);
            });

        await _modelManager.LoadModelAsync(modelA);
        await _modelManager.LoadModelAsync(modelB);

        using var leaseA1 = await _modelManager.AcquireContextAsync("model-a");
        using var leaseB1 = await _modelManager.AcquireContextAsync("model-b");
        Assert.NotNull(leaseB1);
        Assert.Equal(2, _modelManager.GetMetrics().ActiveLeases);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _modelManager.AcquireContextAsync("model-a", cts.Token));
    }

    [Fact]
    public async Task UnloadModelAsync_OnlyUnloadsTargetModel_PreservingOtherLoadedModels()
    {
        var modelA = new ModelSettings { RepoId = "model-a", MaxContexts = 2 };
        var modelB = new ModelSettings { RepoId = "model-b", MaxContexts = 2 };

        await _modelManager.LoadModelAsync(modelA);
        await _modelManager.LoadModelAsync(modelB);

        await _modelManager.UnloadModelAsync("model-a");

        Assert.Null(_modelManager.GetModelSettings("model-a"));
        Assert.NotNull(_modelManager.GetModelSettings("model-b"));
        _mockModelProvider.Verify(p => p.UnloadModel("model-a"), Times.Once);
        _mockModelProvider.Verify(p => p.UnloadModel("model-b"), Times.Never);
    }

    public void Dispose()
    {
        _modelManager.Dispose();
    }
}
