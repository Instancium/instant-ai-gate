// File: src/InstantAIGate.Core/Services/Inference/ModelManager.cs
namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Coordinates the single-model state, graceful draining, and context acquisition.
/// </summary>
public sealed class ModelManager : IDisposable, IModelManager
{
    private readonly IModelProvider _modelProvider;
    private readonly IModelLocator _modelLocator;
    private readonly RequestQueue _requestQueue;
    private readonly ILogger<ModelManager> _logger;

    private ModelSettings? _activeConfig;
    private int _activeLeases;
    private bool _isDraining;
    private readonly SemaphoreSlim _globalLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the model manager.
    /// </summary>
    public ModelManager(
        IModelProvider modelProvider,
        IModelLocator modelLocator,
        RequestQueue requestQueue,
        ILogger<ModelManager> logger)
    {
        _modelProvider = modelProvider;
        _modelLocator = modelLocator;
        _requestQueue = requestQueue;
        _logger = logger;
        _activeLeases = 0;
        _isDraining = false;
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

            var resolvedPaths = await _modelLocator.ResolvePathsAsync(config, ct);

            config = config with
            {
                ModelPath = resolvedPaths.PrimaryModelPath,
                ProjectorPath = resolvedPaths.VisionProjectorPath
            };

            await _modelProvider.InitializeAsync(config, ct);
            _activeConfig = config;
            _requestQueue.Resume();
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
        _requestQueue.Pause();
        _isDraining = true;

        while (Volatile.Read(ref _activeLeases) > 0)
        {
            await Task.Delay(100, ct);
        }

        if (_activeConfig != null)
        {
            _modelProvider.UnloadModel(_activeConfig.RepoId);
        }

        var resolvedPaths = await _modelLocator.ResolvePathsAsync(newConfig, ct);
        newConfig = newConfig with
        {
            ModelPath = resolvedPaths.PrimaryModelPath,
            ProjectorPath = resolvedPaths.VisionProjectorPath
        };

        await _modelProvider.InitializeAsync(newConfig, ct);
        _activeConfig = newConfig;
        _isDraining = false;
        _requestQueue.Resume();
        _logger.LogInformation("Hot-Swap to '{RepoId}' completed successfully.", newConfig.RepoId);
    }

    public async Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default)
    {
        if (_activeConfig == null || _activeConfig.RepoId != repoId || _isDraining)
        {
            throw new InvalidOperationException($"Model '{repoId}' is not active or is currently draining.");
        }

        Interlocked.Increment(ref _activeLeases);

        try
        {
            var inferenceContext = await _modelProvider.GetInferenceContextAsync(repoId, ct);

            if (inferenceContext.TextContext != null)
            {
                inferenceContext.TextContext.AttachOnDispose(() => Interlocked.Decrement(ref _activeLeases));
                return inferenceContext;
            }

            throw new InvalidCastException("Internal infrastructure error while casting contexts.");
        }
        catch
        {
            Interlocked.Decrement(ref _activeLeases);
            throw;
        }
    }

    public async Task UnloadModelAsync(string repoId, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            if (_activeConfig?.RepoId != repoId)
            {
                return;
            }

            _requestQueue.Pause();
            _isDraining = true;

            while (Volatile.Read(ref _activeLeases) > 0)
            {
                await Task.Delay(100, ct);
            }

            _modelProvider.UnloadModel(repoId);
            _activeConfig = null;
            _isDraining = false;
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default)
    {
        var modelWeights = await _modelProvider.GetWeightsAsync(repoId, ct);

        if (modelWeights != null)
        {
            return modelWeights;
        }

        throw new InvalidCastException("Weights infrastructure cannot be mapped.");
    }

    public ModelSettings? GetActiveSettings()
    {
        return _activeConfig;
    }

    public InferenceMetrics GetMetrics()
    {
        int currentLeases = Volatile.Read(ref _activeLeases);
        int pendingRequests = _requestQueue.PendingCount;
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