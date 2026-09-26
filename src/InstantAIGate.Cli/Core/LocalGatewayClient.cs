using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace InstantAIGate.Cli.Core;

public class LocalGatewayClient : IGatewayClient
{
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IModelManager _modelManager;
    private readonly IModelCatalogService _catalogService;
    private readonly IConfiguration _configuration;

    public LocalGatewayClient(
        IInferenceEngine inferenceEngine,
        IModelManager modelManager,
        IModelCatalogService catalogService,
        IConfiguration configuration)
    {
        _inferenceEngine = inferenceEngine;
        _modelManager = modelManager;
        _catalogService = catalogService;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
            string repoId,
            IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = new InferenceSettings
        {
            MaxTokens = 4096,
            Temperature = 0.7f,
            TopP = 0.9f
        };

        // Apply the model's native chat template to the entire history
        string formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(repoId, messages, ct);

        // Extract media parts from the latest user message
        var lastMessage = System.Linq.Enumerable.LastOrDefault(messages, m => m.Role == "user");
        var parts = lastMessage?.Parts ?? new List<MessageContent>();

        await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(repoId, formattedPrompt, parts, settings, ct))
        {
            yield return chunk;
        }
    }

    public Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics,
        Action<DownloadProgress> onSsrProgress,
        CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var metrics = _modelManager.GetMetrics();
                    onMetrics(metrics);
                }
                catch { }
                await Task.Delay(1000, ct);
            }
        }, ct);
        return Task.CompletedTask;
    }

    public async Task LoadModelAsync(string repoId, CancellationToken ct = default)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(repoId, ct);
        if (targetModel == null)
        {
            throw new InvalidOperationException($"Model '{repoId}' not found in catalog.");
        }

        var hwProfile = _configuration.GetSection("InstantAIGate:HardwareProfiles:Default").Get<HardwareProfileSettings>() ?? new HardwareProfileSettings();

        var config = new ModelSettings
        {
            RepoId = targetModel.Id,
            VisionSupport = targetModel.RequiresVisionProjector,
            Type = targetModel.RequiresVisionProjector ? ModelType.Vlm : ModelType.Llm,
            GpuLayerCount = hwProfile.GpuLayerCount,
            MainGPU = hwProfile.MainGPU,
            ContextSize = hwProfile.ContextSize,
            BatchSize = hwProfile.BatchSize,
            Threads = hwProfile.Threads,
            FlashAttention = hwProfile.FlashAttention,
            Embeddings = hwProfile.Embeddings,
            KvCacheQuantization = hwProfile.KvCacheQuantization,
            UseMemoryLock = hwProfile.UseMemoryLock,
            MaxContexts = hwProfile.MaxContexts
        };

        await _modelManager.LoadModelAsync(config, ct);
    }
}