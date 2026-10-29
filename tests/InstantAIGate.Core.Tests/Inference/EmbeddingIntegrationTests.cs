// Native llama.cpp state (loaded DLL, ggml backend registry, CUDA handles) is
// process-wide: a concurrent test class disposing it corrupts inference here.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class EmbeddingIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider _serviceProvider = null!;
    private IModelManager _modelManager = null!;
    private IInferenceEngine _inferenceEngine = null!;
    private static readonly string EmbeddingRepoId = "qwen3-embedding-0.6b";
    private static readonly TestModelOptions ModelOptions = TestConfig.Model;

    public EmbeddingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public ValueTask InitializeAsync()
    {
        bool isNativeLoaded = NativeLibraryLoader.Load();
        Assert.True(isNativeLoaded, "Failed to load llama/mtmd native libraries.");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var storageSettings = new StorageSettings { ModelsDirectory = ModelOptions.ModelsDirectory };
        services.AddSingleton<IOptions<StorageSettings>>(Options.Create(storageSettings));
        services.AddInstantAIGateInference();

        _serviceProvider = services.BuildServiceProvider();
        _modelManager = _serviceProvider.GetRequiredService<IModelManager>();
        _inferenceEngine = _serviceProvider.GetRequiredService<IInferenceEngine>();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _serviceProvider?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithValidText_ReturnsNormalizedVector()
    {
        Assert.True(
            Directory.Exists(Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)),
            $"FATAL: Embedding model directory not found. Expected path: '{Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var config = BuildEmbeddingModelSettings(contextSize: 2048);

        _output.WriteLine("Loading embedding model into VRAM...");
        await _modelManager.LoadModelAsync(config, cts.Token);

        var activeConfig = _modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        Assert.True(activeConfig.Embeddings, "Embeddings must be enabled in model settings");

        try
        {
            string testText = "The quick brown fox jumps over the lazy dog";
            var request = new EmbeddingRequest(testText, Normalize: true);

            _output.WriteLine($"Generating embeddings for: '{testText}'");
            var response = await _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, request, cts.Token);

            Assert.NotNull(response.Embedding);
            Assert.True(response.TokenCount > 0, "Token count must be positive");
            Assert.True(response.EmbeddingDimension > 0, "Embedding dimension must be positive");

            double norm = Math.Sqrt(response.Embedding.Sum(v => (double)v * v));
            Assert.True(Math.Abs(norm - 1.0) < 0.01, $"Normalized vector should have L2 norm ~1.0, got {norm:F4}");

            _output.WriteLine($"Generated {response.EmbeddingDimension}D embedding from {response.TokenCount} tokens");
            _output.WriteLine($"L2 Norm: {norm:F6}");
            _output.WriteLine($"First 10 values: {string.Join(", ", response.Embedding.Take(10).Select(v => v.ToString("F4")))}");
        }
        finally
        {
            await _modelManager.UnloadModelAsync(EmbeddingRepoId, cts.Token);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithSimilarTexts_ReturnsSimilarVectors()
    {
        Assert.True(
            Directory.Exists(Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)),
            $"FATAL: Embedding model directory not found. Expected path: '{Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var config = BuildEmbeddingModelSettings(contextSize: 2048);

        await _modelManager.LoadModelAsync(config, cts.Token);

        try
        {
            string text1 = "The cat sat on the mat";
            string text2 = "A cat is sitting on a mat";
            string text3 = "The stock market crashed today";

            var response1 = await _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, new EmbeddingRequest(text1), cts.Token);
            var response2 = await _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, new EmbeddingRequest(text2), cts.Token);
            var response3 = await _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, new EmbeddingRequest(text3), cts.Token);

            double similarity12 = CosineSimilarity(response1.Embedding, response2.Embedding);
            double similarity13 = CosineSimilarity(response1.Embedding, response3.Embedding);

            _output.WriteLine($"Similarity (text1, text2): {similarity12:F4}");
            _output.WriteLine($"Similarity (text1, text3): {similarity13:F4}");

            Assert.True(similarity12 > similarity13,
                $"Similar texts should have higher cosine similarity. Got {similarity12:F4} vs {similarity13:F4}");
            Assert.True(similarity12 > 0.7, $"Similar texts should have similarity > 0.7, got {similarity12:F4}");
        }
        finally
        {
            await _modelManager.UnloadModelAsync(EmbeddingRepoId, cts.Token);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithEmptyText_ThrowsArgumentException()
    {
        Assert.True(
            Directory.Exists(Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)),
            $"FATAL: Embedding model directory not found. Expected path: '{Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var config = BuildEmbeddingModelSettings(contextSize: 2048);

        await _modelManager.LoadModelAsync(config, cts.Token);

        try
        {
            var request = new EmbeddingRequest("");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, request, cts.Token));
        }
        finally
        {
            await _modelManager.UnloadModelAsync(EmbeddingRepoId, cts.Token);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithTextExceedingContextSize_ThrowsContextOverflowException()
    {
        Assert.True(
            Directory.Exists(Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)),
            $"FATAL: Embedding model directory not found. Expected path: '{Path.Combine(ModelOptions.ModelsDirectory, EmbeddingRepoId)}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // n_ctx must stay >= native n_ubatch (512), otherwise llama_init_from_model
        // rejects the context before the overflow guard can trigger.
        var config = BuildEmbeddingModelSettings(contextSize: 512);

        await _modelManager.LoadModelAsync(config, cts.Token);

        try
        {
            string longText = string.Join(" ", Enumerable.Repeat("word", 600));
            var request = new EmbeddingRequest(longText);

            var ex = await Assert.ThrowsAsync<ContextOverflowException>(() =>
                _inferenceEngine.GenerateEmbeddingsAsync(EmbeddingRepoId, request, cts.Token));

            Assert.Contains("embedding", ex.SessionId);
        }
        finally
        {
            await _modelManager.UnloadModelAsync(EmbeddingRepoId, cts.Token);
        }
    }

    private static ModelSettings BuildEmbeddingModelSettings(int contextSize)
    {
        return new ModelSettings
        {
            RepoId = EmbeddingRepoId,
            VisionSupport = false,
            GpuLayerCount = ModelOptions.ModelLoad.GpuLayerCount,
            ContextSize = contextSize,
            BatchSize = ModelOptions.ModelLoad.BatchSize,
            Threads = ModelOptions.ModelLoad.Threads,
            Embeddings = true,
            FlashAttention = true,
            Type = ModelType.Embedding,
            KvCacheQuantization = "Q8_0"
        };
    }

    private static double CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Vectors must have same dimension");

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        double denominator = Math.Sqrt(norm1) * Math.Sqrt(norm2);
        return denominator > 0 ? dotProduct / denominator : 0;
    }
}