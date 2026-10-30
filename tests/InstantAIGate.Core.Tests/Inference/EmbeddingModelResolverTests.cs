namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

public sealed class EmbeddingModelResolverTests
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly EmbeddingModelResolver _resolver;

    public EmbeddingModelResolverTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _resolver = new EmbeddingModelResolver(_mockModelManager.Object, NullLogger<EmbeddingModelResolver>.Instance);
    }

    private void SetupActiveModels(params ModelSettings[] models)
    {
        var dictionary = new Dictionary<string, ModelSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            dictionary[model.RepoId] = model;
        }

        _mockModelManager.Setup(m => m.ActiveModels).Returns(dictionary);
        _mockModelManager.Setup(m => m.GetModelSettings(It.IsAny<string>()))
            .Returns((string repoId) => dictionary.TryGetValue(repoId, out var settings) ? settings : null);
    }

    [Fact]
    public void Resolve_WhenRequestedModelIsEmbeddingCapable_ReturnsRequestedModel()
    {
        SetupActiveModels(
            new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false },
            new ModelSettings { RepoId = "qwen3-embedding-0.6b", Embeddings = true });

        string resolved = _resolver.Resolve("qwen3-embedding-0.6b");

        Assert.Equal("qwen3-embedding-0.6b", resolved);
    }

    [Fact]
    public void Resolve_WhenRequestedModelIsChat_AutoRoutesToDedicatedEmbeddingModel()
    {
        SetupActiveModels(
            new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false },
            new ModelSettings { RepoId = "qwen3-embedding-0.6b", Embeddings = true });

        string resolved = _resolver.Resolve("qwen3-vl-8b-instruct");

        Assert.Equal("qwen3-embedding-0.6b", resolved);
    }

    [Fact]
    public void Resolve_WhenRequestedChatModelAndNoEmbeddingLoaded_ThrowsNotConfigured()
    {
        SetupActiveModels(new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false });

        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.Resolve("qwen3-vl-8b-instruct"));

        Assert.Contains("not configured for embeddings", ex.Message);
    }

    [Fact]
    public void Resolve_WhenRequestedModelNotActive_AndEmbeddingLoaded_FallsBackToEmbedding()
    {
        SetupActiveModels(
            new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false },
            new ModelSettings { RepoId = "qwen3-embedding-0.6b", Embeddings = true });

        string resolved = _resolver.Resolve("missing-model");

        Assert.Equal("qwen3-embedding-0.6b", resolved);
    }

    [Fact]
    public void Resolve_WhenRequestedModelNotActive_AndNoEmbeddingLoaded_ThrowsNotActive()
    {
        SetupActiveModels(new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false });

        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.Resolve("missing-model"));

        Assert.Contains("is not active", ex.Message);
    }

    [Fact]
    public void Resolve_WhenNoRequestedModel_ReturnsDedicatedEmbeddingModel()
    {
        SetupActiveModels(
            new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false },
            new ModelSettings { RepoId = "qwen3-embedding-0.6b", Type = ModelType.Embedding, Embeddings = true });

        string resolved = _resolver.Resolve(string.Empty);

        Assert.Equal("qwen3-embedding-0.6b", resolved);
    }

    [Fact]
    public void Resolve_WhenNoRequestedModelAndNoEmbeddingLoaded_Throws()
    {
        SetupActiveModels(new ModelSettings { RepoId = "qwen3-vl-8b-instruct", Embeddings = false });

        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(null));
    }
}
