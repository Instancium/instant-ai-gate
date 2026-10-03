namespace InstantAIGate.Cli.Core;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class LocalGatewayClient : IGatewayClient
{
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IModelManager _modelManager;
    private readonly IModelCatalogService _catalogService;
    private readonly IModelDownloader _modelDownloader;
    private readonly IConfiguration _configuration;
    private readonly ISessionInferenceManager _sessionManager;
    private readonly StorageSettings _storageSettings;

    public event Action<int>? QueuePositionReceived;
    public event Action<string, string, string>? LogReceived;

    public LocalGatewayClient(
        IInferenceEngine inferenceEngine,
        IModelManager modelManager,
        IModelCatalogService catalogService,
        IModelDownloader modelDownloader,
        IConfiguration configuration,
        ISessionInferenceManager sessionManager,
        IOptions<StorageSettings> storageSettings)
    {
        _inferenceEngine = inferenceEngine;
        _modelManager = modelManager;
        _catalogService = catalogService;
        _modelDownloader = modelDownloader;
        _configuration = configuration;
        _sessionManager = sessionManager;
        _storageSettings = storageSettings.Value;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId, string repoId, ChatMessage deltaMessage, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessionManager.TryGetSessionRepoId(sessionId, out _))
        {
            await _sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, repoId), ct);
        }

        var activeSettings = _modelManager.GetActiveSettings();
        int contextSize = activeSettings?.ContextSize > 0 ? activeSettings.ContextSize : 4096;
        int dynamicMaxTokens = Math.Clamp((int)(contextSize * 0.25), 256, 1024);

        var settings = new InferenceSettings
        {
            MaxTokens = dynamicMaxTokens,
            Temperature = 0.7f,
            TopP = 0.9f
        };

        await foreach (var chunk in _inferenceEngine.StreamDeltaGenerationAsync(sessionId, deltaMessage, settings, ct))
        {
            yield return chunk;
        }
    }

    public Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        return _sessionManager.ReleaseSessionAsync(sessionId, ct);
    }

    public Task ConnectTelemetryAsync(
        Action<InferenceMetrics> onMetrics, Action<DownloadProgress> onSsrProgress, CancellationToken ct)
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
                catch
                {
                }
                await Task.Delay(1000, ct);
            }
        }, ct);

        return Task.CompletedTask;
    }

    public async Task LoadModelAsync(string repoId, CancellationToken ct = default)
    {
        var config = await BuildModelSettingsAsync(repoId, profile: null, ct);
        await _modelManager.LoadModelAsync(config, ct);
    }

    public Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        return _modelManager.UnloadModelAsync(repoId, ct);
    }

    public async Task SwapModelAsync(string repoId, string? profile = null, CancellationToken ct = default)
    {
        var config = await BuildModelSettingsAsync(repoId, profile, ct);
        await _modelManager.SwapModelAsync(config, ct);
    }

    public async Task DownloadModelAsync(string repoId, CancellationToken ct = default)
    {
        var model = await _catalogService.FindModelByIdAsync(repoId, ct);
        if (model == null)
        {
            throw new InvalidOperationException($"Model '{repoId}' not found in catalog.");
        }

        var downloadUrls = new List<string>(model.DownloadUrls);
        if (model.RequiresVisionProjector && model.VisionProjectorUrls != null)
        {
            downloadUrls.AddRange(model.VisionProjectorUrls);
        }

        string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, model.Id);
        var progressReporter = new Progress<DownloadProgress>();

        await _modelDownloader.DownloadModelAsync(model.Id, downloadUrls, destinationDir, progressReporter, ct);
    }

    public Task SubscribeToModelDownloadAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_modelManager.GetActiveModelDetails());
    }

    public Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct = default)
    {
        return Task.FromResult(_sessionManager.GetPastTokensCount(sessionId));
    }

    public Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct = default)
    {
        return _sessionManager.RollbackToPositionAsync(sessionId, targetPosition, ct);
    }

    public Task ShiftSessionMemoryAsync(string sessionId, int startPos, int count, CancellationToken ct = default)
    {
        return _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos, count, ct);
    }

    private async Task<ModelSettings> BuildModelSettingsAsync(string repoId, string? profile, CancellationToken ct)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(repoId, ct);
        if (targetModel == null)
        {
            throw new InvalidOperationException($"Model '{repoId}' not found in catalog.");
        }

        var profileName = profile ?? "Default";
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}").Get<HardwareProfileSettings>()
                        ?? new HardwareProfileSettings();

        return new ModelSettings
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
    }
}