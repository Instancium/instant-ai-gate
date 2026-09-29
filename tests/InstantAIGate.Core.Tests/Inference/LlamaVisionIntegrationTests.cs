namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
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
    private string _testImagePath = string.Empty;

    // All model/inference parameters are read from the test project appsettings.json.
    private static readonly TestModelOptions ModelOptions = TestConfig.Model;

    public LlamaVisionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        bool isNativeLoaded = NativeLibraryLoader.Load();
        Assert.True(isNativeLoaded, "Failed to load llama/mtmd native libraries.");

        _testImagePath = Path.Combine(AppContext.BaseDirectory, "TestData", "test-1.jpeg");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var storageSettings = new StorageSettings
        {
            ModelsDirectory = ModelOptions.ModelsDirectory
        };

        services.AddSingleton<IOptions<StorageSettings>>(Options.Create(storageSettings));
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
        string expectedModelDirectory = Path.Combine(ModelOptions.ModelsDirectory, ModelOptions.RepoId);

        Assert.True(Directory.Exists(expectedModelDirectory),
            $"FATAL: Model directory not found. Expected path: '{expectedModelDirectory}'");

        Assert.True(File.Exists(_testImagePath),
            $"FATAL: Test image not found. Expected path: '{_testImagePath}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var config = new ModelSettings
        {
            RepoId = ModelOptions.RepoId,
            VisionSupport = ModelOptions.ModelLoad.VisionSupport,
            GpuLayerCount = ModelOptions.ModelLoad.GpuLayerCount,
            ContextSize = ModelOptions.ModelLoad.ContextSize,
            BatchSize = ModelOptions.ModelLoad.BatchSize,
            Threads = ModelOptions.ModelLoad.Threads,
            Type = ModelType.Vlm
        };

        _output.WriteLine("Loading model into VRAM...");
        await _modelManager.LoadModelAsync(config, cts.Token);

        var activeConfig = _modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);

        var parts = new List<MessageContent>
        {
            new ImageFileContent(_testImagePath),
            new TextContent(ModelOptions.Inference.VisionPrompt)
        };

        var chatHistory = new List<ChatMessage>
        {
            new ChatMessage("user", parts)
        };

        _output.WriteLine("Applying Chat Template...");

        string formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(
            config.RepoId, chatHistory, cts.Token);

        Assert.Contains("<__media__>\n", formattedPrompt);

        _output.WriteLine("Starting Inference...");

        var settings = new InferenceSettings
        {
            MaxTokens = ModelOptions.Inference.MaxTokens,
            Temperature = ModelOptions.Inference.Temperature
        };
        var responseBuilder = new StringBuilder();

        var mediaParts = parts.Where(p => p is not TextContent).ToList();

        await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(config.RepoId, formattedPrompt, mediaParts, settings, cts.Token))
        {
            responseBuilder.Append(chunk);
            _output.WriteLine($"Chunk: {chunk.Replace("\n", "\\n")}");
        }

        string fullResponse = responseBuilder.ToString();
        Assert.False(string.IsNullOrWhiteSpace(fullResponse), "The model returned an empty response.");
        _output.WriteLine($"\nFinal Response:\n{fullResponse}");

        // Expected answer keywords come from the test configuration
        foreach (var keyword in ModelOptions.Inference.ExpectedKeywords)
        {
            Assert.Contains(keyword, fullResponse, StringComparison.OrdinalIgnoreCase);
        }

        await _modelManager.UnloadModelAsync(config.RepoId, cts.Token);
    }
}