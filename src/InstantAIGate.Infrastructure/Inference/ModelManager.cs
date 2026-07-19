using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Dtos.Config;
using InstantAIGate.Infrastructure.Inference.layers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Coordinates the single-model state, graceful draining, and context acquisition.
    /// </summary>
    public sealed class ModelManager : IDisposable, IModelManager
    {
        private readonly ModelProvider _modelProvider;
        private readonly IModelPathProvider _pathProvider;
        private readonly RequestQueue _requestQueue;
        private readonly ILogger<ModelManager> _logger;

        private ModelSettings? _activeConfig;
        private int _activeLeases;
        private bool _isDraining;
        private readonly SemaphoreSlim _globalLock = new(1, 1);

        public ModelManager(
            ModelProvider modelProvider,
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
                config.ModelPath = resolvedPath;

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

            string resolvedPath = await _pathProvider.GetFullModelPathAsync(newConfig.RepoId);
            newConfig.ModelPath = resolvedPath;

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
            var llamaModel = await _modelProvider.GetWeightsAsync(repoId, ct);

            if (llamaModel != null)
            {
                return llamaModel;
            }

            throw new InvalidCastException("Weights infrastructure cannot be mapped.");
        }

        /// <summary>
        /// Gets the configuration of the currently active model.
        /// </summary>
        public ModelSettings? GetActiveSettings()
        {
            return _activeConfig;
        }

        /// <summary>
        /// Gets the current throughput and queue metrics for telemetry.
        /// Uses Volatile.Read to safely access the active leases counter across threads.
        /// </summary>
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
}