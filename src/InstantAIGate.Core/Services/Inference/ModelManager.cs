namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, ModelSettings> _activeModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _contextSemaphores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _modelLeases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _drainingModels = new(StringComparer.OrdinalIgnoreCase);

    private int _totalActiveLeases;
    private volatile string? _primaryRepoId;
    private readonly SemaphoreSlim _globalLock = new(1, 1);

    public IReadOnlyDictionary<string, ModelSettings> ActiveModels => _activeModels;

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
    }

    public Task<int> GetEmbeddingDimensionAsync(string repoId, CancellationToken ct = default)
    {
        if (_activeModels.IsEmpty)
            throw new InvalidOperationException("No model is currently active.");

        if (!_activeModels.ContainsKey(repoId))
        {
            string activeList = string.Join(", ", _activeModels.Keys);
            throw new InvalidOperationException(
                $"Model '{repoId}' is not active. Active models: '{activeList}'.");
        }

        int dimension = _modelProvider.GetEmbeddingDimension(repoId);
        return Task.FromResult(dimension);
    }

    public Task PurgeIdleContextsAsync(string repoId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            foreach (var key in _activeModels.Keys)
            {
                _modelProvider.PurgeIdleContexts(key);
            }
        }
        else
        {
            _modelProvider.PurgeIdleContexts(repoId);
        }

        return Task.CompletedTask;
    }

    public ModelSettings? GetModelSettings(string repoId)
    {
        if (string.IsNullOrWhiteSpace(repoId))
            return null;

        return _activeModels.TryGetValue(repoId, out var settings) ? settings : null;
    }

    public ModelSettings? GetActiveSettings(string? repoId = null)
    {
        if (!string.IsNullOrWhiteSpace(repoId))
        {
            return GetModelSettings(repoId);
        }

        if (!string.IsNullOrWhiteSpace(_primaryRepoId) &&
            _activeModels.TryGetValue(_primaryRepoId, out var primary))
        {
            return primary;
        }

        return _activeModels.Values.FirstOrDefault(m => !m.Embeddings && m.Type != ModelType.Embedding)
            ?? _activeModels.Values.FirstOrDefault();
    }

    public NativeModelDetails GetActiveModelDetails(string? repoId = null)
    {
        var targetConfig = GetActiveSettings(repoId);
        if (targetConfig == null)
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
            .FirstOrDefault(d => d.RepoId.Equals(targetConfig.RepoId, StringComparison.OrdinalIgnoreCase));

        return nativeDetails ?? new NativeModelDetails
        {
            RepoId = targetConfig.RepoId,
            ContextSize = targetConfig.ContextSize,
            GpuLayers = targetConfig.GpuLayerCount,
            Threads = targetConfig.Threads,
            FlashAttention = targetConfig.FlashAttention,
            IdleContextsCount = 0,
            Backend = "llama.cpp"
        };
    }

    public async Task LoadModelAsync(ModelSettings config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.RepoId))
            throw new ArgumentException("RepoId cannot be empty.", nameof(config));

        await _globalLock.WaitAsync(ct);
        try
        {
            if (_activeModels.ContainsKey(config.RepoId))
            {
                _logger.LogInformation("Model '{RepoId}' is already loaded in memory.", config.RepoId);
                return;
            }

            var resolvedPaths = await _modelLocator.ResolvePathsAsync(config.RepoId, config.VisionSupport, ct);
            await _modelProvider.InitializeAsync(config, resolvedPaths, ct);

            int maxContexts = config.MaxContexts > 0 ? config.MaxContexts : 4;
            _contextSemaphores[config.RepoId] = new SemaphoreSlim(maxContexts, maxContexts);
            _modelLeases[config.RepoId] = 0;
            _drainingModels[config.RepoId] = false;
            _activeModels[config.RepoId] = config;

            if (_primaryRepoId == null || (!config.Embeddings && config.Type != ModelType.Embedding))
            {
                _primaryRepoId = config.RepoId;
            }

            _queueManager.Resume();
            _eventSource?.NotifyStateChanged();
            _logger.LogInformation("Model '{RepoId}' loaded into active registry (MaxContexts={MaxContexts}).",
                config.RepoId, maxContexts);
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
            _logger.LogInformation("Initiating Swap to '{RepoId}'.", newConfig.RepoId);

            var existingSameType = _activeModels.Values
                .Where(m => m.Type == newConfig.Type && !string.Equals(m.RepoId, newConfig.RepoId, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.RepoId)
                .ToList();

            foreach (var oldRepoId in existingSameType)
            {
                await UnloadModelInternalAsync(oldRepoId, ct);
            }

            if (!_activeModels.ContainsKey(newConfig.RepoId))
            {
                var resolvedPaths = await _modelLocator.ResolvePathsAsync(newConfig.RepoId, newConfig.VisionSupport, ct);
                await _modelProvider.InitializeAsync(newConfig, resolvedPaths, ct);

                int maxContexts = newConfig.MaxContexts > 0 ? newConfig.MaxContexts : 4;
                _contextSemaphores[newConfig.RepoId] = new SemaphoreSlim(maxContexts, maxContexts);
                _modelLeases[newConfig.RepoId] = 0;
                _drainingModels[newConfig.RepoId] = false;
                _activeModels[newConfig.RepoId] = newConfig;
            }

            _primaryRepoId = newConfig.RepoId;
            _queueManager.Resume();
            _eventSource?.NotifyStateChanged();
            _logger.LogInformation("Swap to '{RepoId}' completed successfully.", newConfig.RepoId);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default)
    {
        if (!_activeModels.ContainsKey(repoId) ||
            (_drainingModels.TryGetValue(repoId, out bool isDraining) && isDraining))
        {
            throw new InvalidOperationException($"Model '{repoId}' is not active or is currently draining.");
        }

        if (!_contextSemaphores.TryGetValue(repoId, out var semaphore))
        {
            throw new InvalidOperationException($"Context semaphore for model '{repoId}' was not found.");
        }

        await semaphore.WaitAsync(ct);

        Interlocked.Increment(ref _totalActiveLeases);
        _modelLeases.AddOrUpdate(repoId, 1, (_, current) => current + 1);
        _eventSource?.NotifyStateChanged();

        try
        {
            var inferenceContext = await _modelProvider.GetInferenceContextAsync(repoId, ct);
            if (inferenceContext.TextContext != null)
            {
                inferenceContext.TextContext.AttachOnDispose(() =>
                {
                    _modelLeases.AddOrUpdate(repoId, 0, (_, current) => Math.Max(0, current - 1));
                    Interlocked.Decrement(ref _totalActiveLeases);
                    try
                    {
                        semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    _eventSource?.NotifyStateChanged();
                });
                return inferenceContext;
            }

            throw new InvalidCastException("Internal infrastructure error while casting contexts.");
        }
        catch
        {
            _modelLeases.AddOrUpdate(repoId, 0, (_, current) => Math.Max(0, current - 1));
            Interlocked.Decrement(ref _totalActiveLeases);
            try
            {
                semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
            }
            _eventSource?.NotifyStateChanged();
            throw;
        }
    }

    public async Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            await UnloadModelInternalAsync(repoId, ct);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    private async Task UnloadModelInternalAsync(string repoId, CancellationToken ct)
    {
        if (!_activeModels.ContainsKey(repoId))
            return;

        _drainingModels[repoId] = true;
        _eventSource?.NotifyStateChanged();

        while (_modelLeases.TryGetValue(repoId, out int leases) && leases > 0)
        {
            await Task.Delay(50, ct);
        }

        _modelProvider.UnloadModel(repoId);
        _activeModels.TryRemove(repoId, out _);
        _drainingModels.TryRemove(repoId, out _);
        _modelLeases.TryRemove(repoId, out _);

        if (_contextSemaphores.TryRemove(repoId, out var sem))
        {
            sem.Dispose();
        }

        if (string.Equals(_primaryRepoId, repoId, StringComparison.OrdinalIgnoreCase))
        {
            _primaryRepoId = _activeModels.Keys.FirstOrDefault();
        }

        _eventSource?.NotifyStateChanged();
    }

    public async Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default)
    {
        var modelWeights = await _modelProvider.GetWeightsAsync(repoId, ct);
        if (modelWeights != null) return modelWeights;
        throw new InvalidCastException("Weights infrastructure cannot be mapped.");
    }

    public InferenceMetrics GetMetrics()
    {
        int currentLeases = Volatile.Read(ref _totalActiveLeases);
        int pendingRequests = _queueManager.PendingCount;
        return new InferenceMetrics(currentLeases, pendingRequests);
    }

    public IEnumerable<ModelRegistryStatus> GetActiveModelsStatus() => _modelProvider.GetStatus();
    public IEnumerable<string> GetActiveModels() => _activeModels.Keys.ToList();
    public IEnumerable<NativeModelDetails> GetNativeDetails() => _modelProvider.GetNativeDetails();

    public void Dispose()
    {
        _globalLock.Dispose();
        foreach (var sem in _contextSemaphores.Values)
        {
            sem.Dispose();
        }
        _contextSemaphores.Clear();
        _modelProvider.Dispose();
    }
}
