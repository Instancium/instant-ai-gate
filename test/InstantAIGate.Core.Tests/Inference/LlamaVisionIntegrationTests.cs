namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.Configuration;
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

    // Динамические переменные вместо констант
    private string _testModelsDir = string.Empty;
    private string _testRepoId = string.Empty;
    private string _testImagePath = string.Empty;

    public LlamaVisionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        bool isNativeLoaded = NativeLibraryLoader.Load();
        Assert.True(isNativeLoaded, "Failed to load llama/mtmd native libraries.");

        // 1. Собираем конфигурацию тестов (JSON + Environment Variables)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // 2. Читаем пути (ENV перекрывает JSON, если задан)
        _testModelsDir = configuration["TEST_MODELS_DIR"]
            ?? configuration["InstantAIGate:Storage:ModelsDirectory"]
            ?? @"C:\models";

        _testRepoId = configuration["InstantAIGate:TestData:VisionRepoId"]
            ?? "qwen3-vl-8b-instruct";

        string imageFileName = configuration["InstantAIGate:TestData:VisionTestImage"]
            ?? "test-1.jpeg";

        _testImagePath = Path.Combine(_testModelsDir, imageFileName);

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 3. Передаем динамический путь в DI контейнер
        var storageSettings = new StorageSettings
        {
            ModelsDirectory = _testModelsDir
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
        string expectedModelDirectory = Path.Combine(_testModelsDir, _testRepoId);

        // Жесткие ассерты без "return;" (согласно нашим предыдущим исправлениям)
        Assert.True(Directory.Exists(expectedModelDirectory),
            $"FATAL: Model directory not found. Expected path: '{expectedModelDirectory}'");

        Assert.True(File.Exists(_testImagePath),
            $"FATAL: Test image not found. Expected path: '{_testImagePath}'");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var config = new ModelSettings
        {
            RepoId = _testRepoId,
            VisionSupport = true,
            GpuLayerCount = 99,
            ContextSize = 4096,
            BatchSize = 512,
            Threads = 8,
            Type = ModelType.Vlm
        };

        _output.WriteLine("Loading model into VRAM...");
        await _modelManager.LoadModelAsync(config, cts.Token);

        var activeConfig = _modelManager.GetActiveSettings();
        Assert.NotNull(activeConfig);
        

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
