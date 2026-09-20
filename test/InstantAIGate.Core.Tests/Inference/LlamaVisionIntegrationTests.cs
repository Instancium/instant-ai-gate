// File: test/InstantAIGate.Core.Tests/Inference/LlamaVisionIntegrationTests.cs
namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.Inference.Stubs;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions; 
using Microsoft.Extensions.Logging;
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

    // Hardcoded paths for the local test environment
    private const string TestModelPath = @"C:\models\Qwen_Qwen3-VL-8B-Instruct-GGUF\Qwen3VL-8B-Instruct-Q4_K_M.gguf";
    private const string TestProjectorPath = @"C:\models\Qwen_Qwen3-VL-8B-Instruct-GGUF\mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf";
    private const string TestImagePath = @"C:\models\test-1.jpeg";

    public LlamaVisionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        // 1. Load native libraries manually for the test runner environment
        bool isNativeLoaded = NativeLibraryLoader.Load();
        Assert.True(isNativeLoaded, "Failed to load llama/mtmd native libraries.");

        // 2. Setup Dependency Injection mimicking Program.cs
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddDebug(); // Route logs to test output
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddInstantAIGateInference(); // Load Core & Native facades

        // Safely replace the registered LlamaModelLocator with the test stub
        services.Replace(ServiceDescriptor.Singleton<IModelLocator>(
            new StubModelLocator(TestModelPath, TestProjectorPath)));

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
        // Skip test gracefully if the local file doesn't exist
        if (!File.Exists(TestModelPath) || !File.Exists(TestImagePath))
        {
            _output.WriteLine("Test skipped because local models or test images were not found.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Arrange: Prepare Configuration
        var config = new ModelSettings
        {
            RepoId = "qwen-vl-test",
            VisionSupport = true,
            GpuLayerCount = 99,
            ContextSize = 4096,
            BatchSize = 512,
            Threads = 8,
            Type = ModelType.Vlm
        };

        // Act 1: Load Model into VRAM
        _output.WriteLine("Loading model into VRAM...");
        await _modelManager.LoadModelAsync(config, cts.Token);

        var activeConfig = _modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        Assert.Equal("qwen-vl-test", activeConfig.RepoId);

        // Arrange: Prepare Chat Request (Strictly English string)
        var chatHistory = new List<ChatMessage>
        {
            new ChatMessage("user", "Please describe in detail what you see in this image.")
        };
        var imagePaths = new List<string> { TestImagePath };

        // Act 2: Apply Template & Tokenize
        _output.WriteLine("Applying Chat Template...");
        string formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(
            config.RepoId, chatHistory, imagePaths, cts.Token);

        Assert.Contains("<__media__>", formattedPrompt); // Ensure image token was injected

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