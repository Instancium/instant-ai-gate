using InstantAIGate.Domain.Dtos.Config;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    public sealed class ModelProvider : IDisposable
    {
        private readonly ILogger<ModelProvider> _logger;
        private readonly OgaHandle _ogaHandle;

        private readonly ConcurrentDictionary<string, Model> _modelCache = new();
        private readonly ConcurrentDictionary<string, Tokenizer> _tokenizerCache = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _initLocks = new();

        public ModelProvider(ILogger<ModelProvider> logger)
        {
            _logger = logger;
            _ogaHandle = new OgaHandle();
        }

        public bool IsLoaded(string repoId) => _modelCache.ContainsKey(repoId);

        public async Task InitializeAsync(ModelSettings config, CancellationToken ct = default)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.RepoId))
            {
                throw new ArgumentException("Config and RepoId are required.", nameof(config));
            }

            var repoId = config.RepoId;
            var initLock = _initLocks.GetOrAdd(repoId, _ => new SemaphoreSlim(1, 1));
            await initLock.WaitAsync(ct);

            try
            {
                if (_modelCache.ContainsKey(repoId))
                {
                    return;
                }

                if (!Directory.Exists(config.ModelPath))
                {
                    throw new DirectoryNotFoundException($"ONNX model directory not found: {config.ModelPath}");
                }

                _logger.LogInformation("Loading ONNX model '{RepoId}' from: {Path}", repoId, config.ModelPath);

                var (model, tokenizer) = await Task.Run(() =>
                {
                    using var onnxConfig = new Config(config.ModelPath);

                    if (config.GpuLayerCount > 0)
                    {
                        onnxConfig.AppendProvider("cuda");
                    }

                    var m = new Model(onnxConfig);
                    var t = new Tokenizer(m);

                    return (m, t);
                }, ct);

                if (_modelCache.TryAdd(repoId, model))
                {
                    _tokenizerCache.TryAdd(repoId, tokenizer);
                    _logger.LogInformation("ONNX model '{RepoId}' successfully loaded into VRAM/RAM.", repoId);
                }
                else
                {
                    tokenizer.Dispose();
                    model.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ONNX model '{RepoId}'.", repoId);
                throw;
            }
            finally
            {
                initLock.Release();
            }
        }

        public (Model, Tokenizer) GetNativeResources(string repoId)
        {
            if (string.IsNullOrWhiteSpace(repoId))
            {
                throw new ArgumentException("RepoId is required.", nameof(repoId));
            }

            if (_modelCache.TryGetValue(repoId, out var model) &&
                _tokenizerCache.TryGetValue(repoId, out var tokenizer))
            {
                return (model, tokenizer);
            }

            throw new InvalidOperationException($"ONNX Model '{repoId}' is not loaded in the provider.");
        }

        public void UnloadModel(string repoId)
        {
            if (_tokenizerCache.TryRemove(repoId, out var tokenizer))
            {
                tokenizer.Dispose();
            }

            if (_modelCache.TryRemove(repoId, out var model))
            {
                model.Dispose();
            }

            _initLocks.TryRemove(repoId, out _);

            _logger.LogInformation("ONNX Model '{RepoId}' was successfully unloaded and memory cleared.", repoId);
        }

        public void Dispose()
        {
            foreach (var tokenizer in _tokenizerCache.Values)
            {
                tokenizer.Dispose();
            }

            foreach (var model in _modelCache.Values)
            {
                model.Dispose();
            }

            _tokenizerCache.Clear();
            _modelCache.Clear();
            _initLocks.Clear();

            _ogaHandle.Dispose();
        }
    }
}