using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Dtos.Config;
using InstantAIGate.Infrastructure.Inference.Context;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace InstantAIGate.Infrastructure.Inference
{
    public sealed class ModelManager : IDisposable, IModelManager
    {
        private readonly ModelProvider _modelProvider;
        private readonly IModelPathProvider _pathProvider;
        private readonly ILogger<ModelManager> _logger;

        private readonly ConcurrentDictionary<string, ModelSettings> _activeModels = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _contextSemaphores = new();
        private readonly SemaphoreSlim _globalLock = new(1, 1);

        public IReadOnlyDictionary<string, ModelSettings> ActiveModels => _activeModels;
        public IReadOnlyDictionary<string, SemaphoreSlim> UserSemaphores => _contextSemaphores;

        public ModelManager(
            ModelProvider modelProvider,
            IModelPathProvider pathProvider,
            ILogger<ModelManager> logger)
        {
            _modelProvider = modelProvider;
            _pathProvider = pathProvider;
            _logger = logger;
        }

        public async Task LoadModelAsync(ModelSettings config, CancellationToken ct = default)
        {
            await _globalLock.WaitAsync(ct);
            try
            {
                if (_modelProvider.IsLoaded(config.RepoId)) return;

                string resolvedPath = await _pathProvider.GetFullModelPathAsync(config.RepoId);
                config.ModelPath = resolvedPath;

                await _modelProvider.InitializeAsync(config, ct);
                _activeModels.TryAdd(config.RepoId, config);

                int maxContexts = config.MaxContexts > 0 ? config.MaxContexts : 1;
                _contextSemaphores[config.RepoId] = new SemaphoreSlim(maxContexts, maxContexts);
            }
            finally
            {
                _globalLock.Release();
            }
        }

        public async Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct = default)
        {
            var semaphore = GetSemaphoreOrThrow(repoId);

            await semaphore.WaitAsync(ct);

            try
            {
                var inferenceContext = await _modelProvider.GetInferenceContextAsync(repoId, ct);

                if (inferenceContext.TextContext != null)
                {
                    inferenceContext.TextContext.AttachOnDispose(() => semaphore.Release());
                    return inferenceContext;
                }

                throw new InvalidCastException("Internal infrastructure error.");
            }
            catch
            {
                semaphore.Release();
                throw;
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

        public async Task UnloadModelAsync(string repoId, CancellationToken ct = default)
        {
            await _globalLock.WaitAsync(ct);
            try
            {
                if (_activeModels.TryRemove(repoId, out _))
                {
                    _modelProvider.UnloadModel(repoId);
                    if (_contextSemaphores.TryRemove(repoId, out var sem)) sem.Dispose();
                }
            }
            finally
            {
                _globalLock.Release();
            }
        }

        private SemaphoreSlim GetSemaphoreOrThrow(string repoId)
        {
            if (!_contextSemaphores.TryGetValue(repoId, out var sem))
                throw new KeyNotFoundException($"Model '{repoId}' not loaded.");
            return sem;
        }

        public void Dispose()
        {
            _globalLock.Dispose();
            foreach (var sem in _contextSemaphores.Values) sem.Dispose();
            _modelProvider.Dispose();
        }

        public IEnumerable<ModelRegistryStatus> GetActiveModelsStatus() => _modelProvider.GetStatus();
        public IEnumerable<string> GetActiveModels() => _activeModels.Keys.ToList();
        public IEnumerable<NativeModelDetails> GetNativeDetails() => _modelProvider.GetNativeDetails();
    }
}