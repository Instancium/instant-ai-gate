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
    private readonly IModelPathProvider _pathProvider;
    private readonly RequestQueue _requestQueue;
    private readonly ILogger<ModelManager> _logger;

    private ModelSettings? _activeConfig;
    private int _activeLeases;
    private bool _isDraining;
    private readonly SemaphoreSlim _globalLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the model manager.
    /// </summary>
    /// <param name="modelProvider">Model provider implementation.</param>
    /// <param name="pathProvider">Model path resolution service.</param>
    /// <param name="requestQueue">Request queue with backpressure.</param>
    /// <param name="logger">Logger instance.</param>
    public ModelManager(
        IModelProvider modelProvider,
        IModelPathProvider pathProvider,
        RequestQueue requestQueue,
        ILogger<ModelManager> logger)
    {
        _modelProvider = modelProvider;
        _pathProvider = pathProvider;
        _requestQueue = requestQueue;
        _logger = logger;
        _activeLeases = 0;
        _isDraining = false;
    }

    /// <summary>
    /// Loads a model with the specified configuration.
    /// </summary>
    /// <param name="config">Model configuration settings.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadModelAsync(ModelSettings config, CancellationToken ct = default)
    {
        await _globalLock.WaitAsync(ct);
        try
        {
            if (_activeConfig?.RepoId == config.RepoId)
            {
                return;
            }

            if (_activeConfig != null)
            {
                await PerformGracefulSwapInternalAsync(config, ct);
                return;
            }

            string resolvedPath = await _pathProvider.GetFullModelPathAsync(config.RepoId);
            config = config with { ModelPath = resolvedPath };

            await _modelProvider.InitializeAsync(config, ct);
            _activeConfig = config;
            _requestQueue.Resume();
        }
        finally
        {
            _globalLock.Release();
        }
    }

    /// <summary>
    /// Performs a graceful hot-swap to a new model configuration.
    /// </summary>
    /// <param name="newConfig">New model configuration.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Executes the internal graceful swap sequence with draining.
    /// </summary>
    /// <param name="newConfig">New model configuration.</param>
    /// <param name="ct">Cancellation token.</param>
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

        string resolvedPath = await _pathProvider.GetFullModelPathAsync(newConfig.RepoId);
        newConfig = newConfig with { ModelPath = resolvedPath };

        await _modelProvider.InitializeAsync(newConfig, ct);
        _activeConfig = newConfig;
        _isDraining = false;
        _requestQueue.Resume();

        _logger.LogInformation("Hot-Swap to '{RepoId}' completed successfully.", newConfig.RepoId);
    }

    /// <summary>
    /// Acquires an inference context for the specified model.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inference context wrapper.</returns>
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

    /// <summary>
    /// Unloads the specified model from memory.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Acquires model weights for direct access.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Model weights accessor.</returns>
    public async Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct = default)
    {
        var modelWeights = await _modelProvider.GetWeightsAsync(repoId, ct);

        if (modelWeights != null)
        {
            return modelWeights;
        }

        throw new InvalidCastException("Weights infrastructure cannot be mapped.");
    }

    /// <summary>
    /// Gets the configuration of the currently active model.
    /// </summary>
    /// <returns>Active model settings or null.</returns>
    public ModelSettings? GetActiveSettings()
    {
        return _activeConfig;
    }

    /// <summary>
    /// Gets the current throughput and queue metrics for telemetry.
    /// Uses Volatile.Read to safely access the active leases counter across threads.
    /// </summary>
    /// <returns>Inference metrics snapshot.</returns>
    public InferenceMetrics GetMetrics()
    {
        int currentLeases = Volatile.Read(ref _activeLeases);
        int pendingRequests = _requestQueue.PendingCount;
        return new InferenceMetrics(currentLeases, pendingRequests);
    }

    /// <summary>
    /// Gets the status of all active models.
    /// </summary>
    /// <returns>Collection of model registry statuses.</returns>
    public IEnumerable<ModelRegistryStatus> GetActiveModelsStatus() => _modelProvider.GetStatus();

    /// <summary>
    /// Gets the list of active model repository identifiers.
    /// </summary>
    /// <returns>Collection of active repo IDs.</returns>
    public IEnumerable<string> GetActiveModels() => _activeConfig != null ? new[] { _activeConfig.RepoId } : Array.Empty<string>();

    /// <summary>
    /// Gets native backend details for all loaded models.
    /// </summary>
    /// <returns>Collection of native model details.</returns>
    public IEnumerable<NativeModelDetails> GetNativeDetails() => _modelProvider.GetNativeDetails();

    /// <summary>
    /// Disposes the model manager and releases resources.
    /// </summary>
    public void Dispose()
    {
        _globalLock.Dispose();
        _modelProvider.Dispose();
    }
}