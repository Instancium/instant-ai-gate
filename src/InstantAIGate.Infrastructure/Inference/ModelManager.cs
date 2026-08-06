using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Domain.Dtos.Config;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    public sealed class ModelManager : IModelManager, IDisposable
    {
        private readonly ModelProvider _modelProvider;
        private readonly ILogger<ModelManager> _logger;

        private ModelSettings? _activeConfig;
        private int _activeLeases;
        private bool _isDraining;
        private readonly SemaphoreSlim _globalLock = new(1, 1);

        public ModelManager(ModelProvider modelProvider, ILogger<ModelManager> logger)
        {
            _modelProvider = modelProvider;
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

                await _modelProvider.InitializeAsync(config, ct);
                _activeConfig = config;
            }
            finally
            {
                _globalLock.Release();
            }
        }

        private async Task PerformGracefulSwapInternalAsync(ModelSettings newConfig, CancellationToken ct)
        {
            _logger.LogInformation("Initiating Hot-Swap to '{RepoId}'.", newConfig.RepoId);

            _isDraining = true;

            while (Volatile.Read(ref _activeLeases) > 0)
            {
                await Task.Delay(100, ct);
            }

            if (_activeConfig != null)
            {
                _modelProvider.UnloadModel(_activeConfig.RepoId);
            }

            await _modelProvider.InitializeAsync(newConfig, ct);

            _activeConfig = newConfig;
            _isDraining = false;

            _logger.LogInformation("Hot-Swap to '{RepoId}' completed successfully.", newConfig.RepoId);
        }

        public Task<IInferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default)
        {
            if (_activeConfig == null || _activeConfig.RepoId != repoId || _isDraining)
            {
                throw new InvalidOperationException($"Model '{repoId}' is not active or is currently draining.");
            }

            Interlocked.Increment(ref _activeLeases);

            try
            {
                var (model, tokenizer) = _modelProvider.GetNativeResources(repoId);

                IInferenceContext context = new OnnxInferenceContext(model, tokenizer, () =>
                {
                    Interlocked.Decrement(ref _activeLeases);
                });

                return Task.FromResult(context);
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

        public ModelSettings? GetActiveSettings()
        {
            return _activeConfig;
        }

        public IEnumerable<string> GetActiveModels()
        {
            return _activeConfig != null ? new[] { _activeConfig.RepoId } : Array.Empty<string>();
        }

        public InferenceMetrics GetMetrics()
        {
            int currentLeases = Volatile.Read(ref _activeLeases);
            return new InferenceMetrics(currentLeases, 0);
        }

        public IEnumerable<ModelRegistryStatus> GetActiveModelsStatus()
        {
            if (_activeConfig == null) yield break;

            yield return new ModelRegistryStatus(
                _activeConfig.RepoId,
                true,
                Volatile.Read(ref _activeLeases),
                _activeConfig.MaxContexts,
                _activeConfig.GpuLayerCount,
                _activeConfig.Type
            );
        }

        public IEnumerable<NativeModelDetails> GetNativeDetails()
        {
            if (_activeConfig == null) yield break;

            yield return new NativeModelDetails
            {
                RepoId = _activeConfig.RepoId,
                ContextSize = _activeConfig.ContextSize,
                GpuLayers = _activeConfig.GpuLayerCount,
                Threads = _activeConfig.Threads,
                FlashAttention = _activeConfig.FlashAttention,
                IdleContextsCount = 0,
                Backend = "onnxruntime-genai"
            };
        }

        public void Dispose()
        {
            _globalLock.Dispose();
            _modelProvider.Dispose();
        }
    }
}