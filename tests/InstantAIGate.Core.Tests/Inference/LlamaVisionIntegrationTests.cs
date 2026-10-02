namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
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

    // 1. MUST ADD THIS FIELD:
    private ISessionInferenceManager _sessionManager = null!;

    private string _testImagePath = string.Empty;

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

        var storageSettings = new StorageSettings { ModelsDirectory = ModelOptions.ModelsDirectory };
        services.AddSingleton<IOptions<StorageSettings>>(Options.Create(storageSettings));

        services.AddInstantAIGateInference();

        _serviceProvider = services.BuildServiceProvider();
        _modelManager = _serviceProvider.GetRequiredService<IModelManager>();
        _inferenceEngine = _serviceProvider.GetRequiredService<IInferenceEngine>();

        // 2. MUST INITIALIZE FROM DI CONTAINER:
        _sessionManager = _serviceProvider.GetRequiredService<ISessionInferenceManager>();

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
        Assert.True(Directory.Exists(expectedModelDirectory), $"FATAL: Model directory not found. Expected path: '{expectedModelDirectory}'");
        Assert.True(File.Exists(_testImagePath), $"FATAL: Test image not found. Expected path: '{_testImagePath}'");

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

        // FIX: Restore the ImageFileContent to trigger the mtmd_helper_eval_chunks routing
        var parts = new List<MessageContent>
        {
            new ImageFileContent(_testImagePath),
            new TextContent(ModelOptions.Inference.VisionPrompt)
        };

        var deltaMessage = new ChatMessage("user", parts);

        _output.WriteLine("Applying Chat Template...");

        string formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(
            config.RepoId, new[] { deltaMessage }, cts.Token);

        _output.WriteLine("Starting Inference...");

        var settings = new InferenceSettings
        {
            MaxTokens = ModelOptions.Inference.MaxTokens,
            Temperature = ModelOptions.Inference.Temperature
        };

        var responseBuilder = new StringBuilder();
        string sessionId = $"vision-test-{Guid.NewGuid():N}";

        await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, config.RepoId), cts.Token);

        try
        {
            await foreach (var chunk in _inferenceEngine.StreamDeltaGenerationAsync(sessionId, deltaMessage, settings, cts.Token))
            {
                responseBuilder.Append(chunk);
                _output.WriteLine($"Chunk: {chunk.Replace("\n", "\\n")}");
            }
        }
        finally
        {
            await _sessionManager.ReleaseSessionAsync(sessionId, CancellationToken.None);
        }

        string fullResponse = responseBuilder.ToString();
        Assert.False(string.IsNullOrWhiteSpace(fullResponse), "The model returned an empty response.");
        _output.WriteLine($"\nFinal Response:\n{fullResponse}");

        foreach (var keyword in ModelOptions.Inference.ExpectedKeywords)
        {
            Assert.Contains(keyword, fullResponse, StringComparison.OrdinalIgnoreCase);
        }

        await _modelManager.UnloadModelAsync(config.RepoId, cts.Token);
    }
}