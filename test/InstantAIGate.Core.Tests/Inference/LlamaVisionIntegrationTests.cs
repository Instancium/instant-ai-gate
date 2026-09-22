// File: test/InstantAIGate.Core.Tests/Inference/LlamaVisionIntegrationTests.cs
namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class LlamaVisionIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider _serviceProvider = null!;
    private IModelManager _modelManager = null!;
    private IInferenceEngine _inferenceEngine = null!;

    // Resolving test directories dynamically (CI/CD friendly)
    //private readonly string _testModelsDir = Environment.GetEnvironmentVariable("TEST_MODELS_DIR")
    //    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InstantAIGate", "models");
    //
    private readonly string _testModelsDir = "C:\\models";
    private const string TestRepoId = "qwen3-vl-8b-instruct";
    private readonly string _testImagePath;

    public LlamaVisionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _testImagePath = Path.Combine(_testModelsDir, "test-1.jpeg");
    }

    public Task InitializeAsync()
    {
        bool isNativeLoaded = NativeLibraryLoader.Load();
        Assert.True(isNativeLoaded, "Failed to load llama/mtmd native libraries.");

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddDebug(); // Route logs to test output
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Inject StorageSettings using object initializer to satisfy 'init' constraint
        var storageSettings = new StorageSettings
        {
            ModelsDirectory = _testModelsDir
        };
        services.AddSingleton<IOptions<StorageSettings>>(Options.Create(storageSettings));

        // Load Core & Native facades (registers the real LlamaModelLocator)
        services.AddInstantAIGateInference();

        _serviceProvider = services.BuildServiceProvider();
        _modelManager = _serviceProvider.GetRequiredService<IModelManager>();
        _inferenceEngine = _serviceProvider.GetRequiredService<IInferenceEngine>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        NativeLibraryLoader.Unload();
        return Task.CompletedTask;
    }


    [Fact]
    public async Task Should_Load_Vision_Model_And_Analyze_Image_Successfully()
    {
        string expectedModelDirectory = Path.Combine(_testModelsDir, TestRepoId);

        // STRICT ASSERTIONS: The test will FAIL immediately if files are missing.
        Assert.True(Directory.Exists(expectedModelDirectory),
            $"FATAL: Model directory not found. Expected path: '{expectedModelDirectory}'");

        Assert.True(File.Exists(_testImagePath),
            $"FATAL: Test image not found. Expected path: '{_testImagePath}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var config = new ModelSettings
        {
            RepoId = TestRepoId,
            VisionSupport = true,
            GpuLayerCount = 99,
            ContextSize = 4096,
            BatchSize = 512,
            Threads = 8,
            Type = ModelType.Vlm
        };

        // ... (остальной код загрузки и инференса остается прежним)

        // Act 1: Load Model into VRAM
        _output.WriteLine("Loading model into VRAM...");

        // The real LlamaModelLocator will search inside expectedModelDirectory for .gguf files
        await _modelManager.LoadModelAsync(config, cts.Token);

        var activeConfig = _modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        Assert.Equal(TestRepoId, activeConfig.RepoId);

        // Arrange: Prepare Chat Request
        var chatHistory = new List<ChatMessage>
        {
            new ChatMessage("user", "Please describe in detail what you see in this image.")
        };
        var imagePaths = new List<string> { _testImagePath };

        // Act 2: Apply Template & Tokenize
        _output.WriteLine("Applying Chat Template...");
        string formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(
            config.RepoId, chatHistory, imagePaths, cts.Token);

        // Assert exact token injection matching LlamaInference.cs implementation
        Assert.Contains("<__media__>\n", formattedPrompt);

        // Act 3: Stream Generation
        _output.WriteLine("Starting Inference...");
        var settings = new InferenceSettings { MaxTokens = 200, Temperature = 0.5f };
        var responseBuilder = new StringBuilder();

        await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(config.RepoId, formattedPrompt, imagePaths, settings, cts.Token))
        {
            responseBuilder.Append(chunk);
            _output.WriteLine($"Chunk: {chunk.Replace("\n", "\\n")}");
        }

        string fullResponse = responseBuilder.ToString();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(fullResponse), "The model returned an empty response.");
        _output.WriteLine($"\nFinal Response:\n{fullResponse}");

        // Cleanup
        await _modelManager.UnloadModelAsync(config.RepoId, cts.Token);
    }
}