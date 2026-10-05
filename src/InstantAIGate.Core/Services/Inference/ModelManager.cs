namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class ModelManager : IDisposable, IModelManager
{
    private readonly IModelProvider _modelProvider;
    private readonly IModelLocator _modelLocator;
    private readonly IQueueManager _queueManager;
    private readonly ILogger<ModelManager> _logger;
    private readonly IMetricsEventSource? _eventSource;
    private ModelSettings? _activeConfig;
    private int _activeLeases;
    private bool _isDraining;
    private readonly SemaphoreSlim _globalLock = new(1, 1);

    public ModelManager(
        IModelProvider modelProvider,
        IModelLocator modelLocator,
        IQueueManager queueManager,
        ILogger<ModelManager> logger,
        IMetricsEventSource? eventSource = null)
    {
        _modelProvider = modelProvider;
        _modelLocator = modelLocator;
        _queueManager = queueManager;
        _logger = logger;
        _eventSource = eventSource;
        _activeLeases = 0;
        _isDraining = false;
    }


    public Task PurgeIdleContextsAsync(string repoId, CancellationToken ct = default)
    {
        _modelProvider.PurgeIdleContexts(repoId);
        return Task.CompletedTask;
    }

    public NativeModelDetails GetActiveModelDetails()
    {
        if (_activeConfig == null)
        {
            return new NativeModelDetails
            {
                RepoId = string.Empty,
                ContextSize = 0,
                GpuLayers = 0,
                Threads = 0,
                FlashAttention = false,
                IdleContextsCount = 0,
                Backend = "none"
            };
        }

        var nativeDetails = _modelProvider.GetNativeDetails()
            .FirstOrDefault(d => d.RepoId.Equals(_activeConfig.RepoId, StringComparison.OrdinalIgnoreCase));

        return nativeDetails ?? new NativeModelDetails
        {
            RepoId = _activeConfig.RepoId,
            ContextSize = _activeConfig.ContextSize,
            GpuLayers = _activeConfig.GpuLayerCount,
            Threads = _activeConfig.Threads,
            FlashAttention = _activeConfig.FlashAttention,
            IdleContextsCount = 0,
            Backend = "llama.cpp"
        };
    }

    public async Task LoadModelAsync(ModelSettings config, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            if (_activeConfig?.RepoId == config.RepoId) return;
            if (_activeConfig != null)
            {
                await PerformGracefulSwapInternalAsync(config, ct);
                return;
            }

            var resolvedPaths = await _modelLocator.ResolvePathsAsync(config.RepoId, config.VisionSupport, ct);
            await _modelProvider.InitializeAsync(config, resolvedPaths, ct);
            _activeConfig = config;
            _queueManager.Resume();
            _eventSource?.NotifyStateChanged();
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task SwapModelAsync(ModelSettings newConfig, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            await PerformGracefulSwapInternalAsync(newConfig, ct);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    private async Task PerformGracefulSwapInternalAsync(ModelSettings newConfig, CancellationToken ct)
    {
        _logger.LogInformation("Initiating Hot-Swap to '{RepoId}'.", newConfig.RepoId);
        _queueManager.Pause();
        _isDraining = true;
        _eventSource?.NotifyStateChanged();

        while (Volatile.Read(ref _activeLeases) > 0)
        {
            await Task.Delay(100, ct);
        }

        if (_activeConfig != null)
        {
            _modelProvider.UnloadModel(_activeConfig.RepoId);
        }

        var resolvedPaths = await _modelLocator.ResolvePathsAsync(newConfig.RepoId, newConfig.VisionSupport, ct);
        await _modelProvider.InitializeAsync(newConfig, resolvedPaths, ct);
        _activeConfig = newConfig;
        _isDraining = false;
        _queueManager.Resume();
        _eventSource?.NotifyStateChanged();

        _logger.LogInformation("Hot-Swap to '{RepoId}' completed successfully.", newConfig.RepoId);
    }

    public async Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default)
    {
        if (_activeConfig == null || _activeConfig.RepoId != repoId || _isDraining)
        {
            throw new InvalidOperationException($"Model '{repoId}' is not active or is currently draining.");
        }

        Interlocked.Increment(ref _activeLeases);
        _eventSource?.NotifyStateChanged();

        try
        {
            var inferenceContext = await _modelProvider.GetInferenceContextAsync(repoId, ct);
            if (inferenceContext.TextContext != null)
            {
                inferenceContext.TextContext.AttachOnDispose(() =>
                {
                    Interlocked.Decrement(ref _activeLeases);
                    _eventSource?.NotifyStateChanged();
                });
                return inferenceContext;
            }

            throw new InvalidCastException("Internal infrastructure error while casting contexts.");
        }
        catch
        {
            Interlocked.Decrement(ref _activeLeases);
            _eventSource?.NotifyStateChanged();
            throw;
        }
    }

    public async Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            if (_activeConfig?.RepoId != repoId) return;

            _queueManager.Pause();
            _isDraining = true;
            _eventSource?.NotifyStateChanged();

            while (Volatile.Read(ref _activeLeases) > 0)
            {
                await Task.Delay(100, ct);
            }

            _modelProvider.UnloadModel(repoId);
            _activeConfig = null;
            _isDraining = false;
            _eventSource?.NotifyStateChanged();
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default)
    {
        var modelWeights = await _modelProvider.GetWeightsAsync(repoId, ct);
        if (modelWeights != null) return modelWeights;
        throw new InvalidCastException("Weights infrastructure cannot be mapped.");
    }

    public ModelSettings? GetActiveSettings() => _activeConfig;

    public InferenceMetrics GetMetrics()
    {
        int currentLeases = Volatile.Read(ref _activeLeases);
        int pendingRequests = _queueManager.PendingCount;
        return new InferenceMetrics(currentLeases, pendingRequests);
    }

    public IEnumerable<ModelRegistryStatus> GetActiveModelsStatus() => _modelProvider.GetStatus();

    public IEnumerable<string> GetActiveModels() => _activeConfig != null ? new[] { _activeConfig.RepoId } : Array.Empty<string>();

    public IEnumerable<NativeModelDetails> GetNativeDetails() => _modelProvider.GetNativeDetails();

    public void Dispose()
    {
        _globalLock.Dispose();
        _modelProvider.Dispose();
    }
}