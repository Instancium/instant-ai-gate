using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Domain.Dtos.Config;
using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Manages model loading, context pooling, and inference lifecycle.
    /// Uses INativeLlamaApi for all native operations and NativeVisionApi for multimodal support.
    /// </summary>
    public class ModelProvider : IDisposable
    {
        private readonly ILogger<ModelProvider> _logger;
        private readonly NativeLlamaApi _nativeApi;
        private readonly NativeVisionApi _visionApi;

        private readonly ConcurrentDictionary<string, IntPtr> _modelCache = new();
        private readonly ConcurrentDictionary<string, VisionContext> _visionCache = new();
        private readonly ConcurrentDictionary<string, ModelSettings> _configCache = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<IntPtr>> _pools = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _initLocks = new();

        private static bool _isBackendInitialized = false;
        private static readonly object _backendLock = new();
        private static NativeLogCallback? _logCallback;
        private static ILogger<ModelProvider>? _staticLogger;
        private static bool _isStderrRedirected = false;
        private static readonly object _stderrLock = new();

        public ModelProvider(ILogger<ModelProvider> logger, NativeLlamaApi nativeApi, NativeVisionApi visionApi)
        {
            _logger = logger;
            _nativeApi = nativeApi;
            _visionApi = visionApi;
            _staticLogger = logger;

            RedirectStderr();
        }

        #region Logging

        private void RedirectStderr()
        {
            lock (_stderrLock)
            {
                if (_isStderrRedirected) return;
                try
                {
                    Console.SetError(new LlamaStderrLogger(_logger));
                    _isStderrRedirected = true;
                }
                catch (Exception ex)
                {
                    _staticLogger?.LogWarning(ex, "Failed to redirect standard error stream.");
                }
            }
        }

        private void SetupLlamaLogging()
        {
            if (_logCallback == null)
            {
                _logCallback = LlamaLogHandler;
                _nativeApi.SetLogCallback(_logCallback);
            }
        }

        private static void LlamaLogHandler(NativeGgmlLogLevel level, string message)
        {
            if (string.IsNullOrEmpty(message) || _staticLogger == null) return;

            switch (level)
            {
                case NativeGgmlLogLevel.Error:
                    _staticLogger.LogError("[llama.cpp] {Message}", message);
                    break;
                case NativeGgmlLogLevel.Warning:
                    _staticLogger.LogWarning("[llama.cpp] {Message}", message);
                    break;
                case NativeGgmlLogLevel.Debug:
                    _staticLogger.LogDebug("[llama.cpp] {Message}", message);
                    break;
                default:
                    _staticLogger.LogInformation("[llama.cpp] {Message}", message);
                    break;
            }
        }

        private class LlamaStderrLogger : TextWriter
        {
            private readonly ILogger _logger;
            private readonly StringBuilder _buffer = new();

            public LlamaStderrLogger(ILogger logger) => _logger = logger;
            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                _buffer.Append(value);
                if (value == '\n') FlushLine();
            }

            public override void Write(string? value)
            {
                if (value is null)
                {
                    return;
                }

                _buffer.Append(value);
                if (value.Contains('\n'))
                {
                    FlushLine();
                }
            }

            private void FlushLine()
            {
                string line = _buffer.ToString().TrimEnd('\n', '\r');
                _buffer.Clear();
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogWarning("[llama.cpp STDERR] {Message}", line);
            }
        }

        #endregion

        public bool IsLoaded(string repoId) => _modelCache.ContainsKey(repoId);

        public async Task InitializeAsync(ModelSettings config, CancellationToken ct = default)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.RepoId))
                throw new ArgumentException("Config and RepoId required.", nameof(config));

            var currentFileName = Path.GetFileName(config.ModelPath);
            if (currentFileName != null &&
               (currentFileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase) ||
                currentFileName.Contains("clip", StringComparison.OrdinalIgnoreCase)))
            {
                var directory = Path.GetDirectoryName(config.ModelPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    var textModel = Directory.GetFiles(directory, "*.gguf")
                        .FirstOrDefault(f => !f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) &&
                                             !f.Contains("clip", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(textModel))
                    {
                        _logger.LogWarning("Auto-corrected config.ModelPath. Switched from projector '{Proj}' to text model '{Text}'",
                            currentFileName, Path.GetFileName(textModel));

                        config.ProjectorPath = config.ModelPath;
                        config.ModelPath = textModel;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to find a valid text model in {directory}. Only projector found.");
                    }
                }
            }

            var repoId = config.RepoId;
            var initLock = _initLocks.GetOrAdd(repoId, _ => new SemaphoreSlim(1, 1));
            await initLock.WaitAsync(ct);

            try
            {
                if (_modelCache.ContainsKey(repoId)) return;

                if (!File.Exists(config.ModelPath))
                    throw new FileNotFoundException("Model file not found.", config.ModelPath);

                var fileInfo = new FileInfo(config.ModelPath);
                long sizeMb = fileInfo.Length / (1024 * 1024);

                if (sizeMb > config.MaxModelFileSizeMb)
                    throw new InvalidOperationException($"Model file too large: {sizeMb} MB > limit {config.MaxModelFileSizeMb} MB");

                lock (_backendLock)
                {
                    if (!_isBackendInitialized)
                    {
                        _logger.LogInformation("Initializing llama.cpp backends...");
                        _nativeApi.LoadAllBackends();
                        _nativeApi.BackendInit();
                        _isBackendInitialized = true;
                        SetupLlamaLogging();

                        try
                        {
                            bool gpuSupport = _nativeApi.SupportsGpuOffload();
                            _logger.LogInformation("GPU offload support: {Support}", gpuSupport ? "YES" : "NO");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to evaluate GPU support.");
                        }
                    }
                }

                var splitMode = config.GpuLayerCount > 0
                    ? NativeLlamaSplitMode.Layer
                    : NativeLlamaSplitMode.None;

                _logger.LogInformation(
                    "Loading model '{RepoId}' | GPU Layers: {Layers} | Main GPU: {Gpu} | Size: {Size} MB | Resolved Path: {Path}",
                    repoId, config.GpuLayerCount, config.MainGPU, sizeMb, config.ModelPath);

                IntPtr modelHandle = _nativeApi.LoadModel(
                    path: config.ModelPath,
                    gpuLayers: config.GpuLayerCount,
                    mainGpu: config.MainGPU,
                    useMlock: config.UseMemoryLock,
                    useMmap: !config.UseMemoryLock,
                    splitMode: splitMode);

                if (modelHandle == IntPtr.Zero)
                    throw new InvalidOperationException($"Native engine returned null handle for '{repoId}'.");

                if (_modelCache.TryAdd(repoId, modelHandle))
                {
                    _configCache.TryAdd(repoId, config);
                    _logger.LogInformation("Model '{RepoId}' loaded successfully with {Layers} GPU layers", repoId, config.GpuLayerCount);

                    if (config.VisionSupport)
                    {
                        try
                        {
                            var visionContext = _visionApi.InitializeVision(config.ProjectorPath!, modelHandle);
                            _visionCache.TryAdd(repoId, visionContext);
                            _logger.LogInformation("Vision projector loaded and bound to '{RepoId}'", repoId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to load vision projector for '{RepoId}'. Unloading base model.", repoId);
                            _nativeApi.FreeModel(modelHandle);
                            _modelCache.TryRemove(repoId, out _);
                            _configCache.TryRemove(repoId, out _);
                            throw;
                        }
                    }
                }
                else
                {
                    _nativeApi.FreeModel(modelHandle);
                }
            }
            finally
            {
                initLock.Release();
            }
        }

        public async Task<InferenceContext> GetInferenceContextAsync(string repoId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(repoId))
                throw new ArgumentException("RepoId required.", nameof(repoId));

            ModelContext textContext;

            if (_pools.TryGetValue(repoId, out var pool) && pool.TryTake(out IntPtr ctxPtr))
            {
                _nativeApi.ClearMemory(_nativeApi.GetMemory(ctxPtr), true);
                textContext = new ModelContext(ctxPtr, ptr => ReturnContextToPool(repoId, ptr.Handle));
            }
            else
            {
                var initLock = _initLocks.GetOrAdd(repoId, _ => new SemaphoreSlim(1, 1));
                await initLock.WaitAsync(ct);
                try
                {
                    if (_pools.TryGetValue(repoId, out pool) && pool.TryTake(out ctxPtr))
                    {
                        _nativeApi.ClearMemory(_nativeApi.GetMemory(ctxPtr), true);
                        textContext = new ModelContext(ctxPtr, ptr => ReturnContextToPool(repoId, ptr.Handle));
                    }
                    else if (_modelCache.TryGetValue(repoId, out IntPtr modelPtr) &&
                        _configCache.TryGetValue(repoId, out var config))
                    {
                        var flashAttn = config.FlashAttention
                            ? NativeLlamaFlashAttnType.Enabled
                            : NativeLlamaFlashAttnType.Disabled;
                        var kvType = ResolveKvCacheType(config.KvCacheQuantization);
                        bool offloadKqv = config.GpuLayerCount > 0;

                        uint nCtx = config.ContextSize > 0 ? (uint)config.ContextSize : 2048;
                        uint nBatch = config.BatchSize > 0 ? (uint)config.BatchSize : 512;
                        int nThreads = config.Threads > 0 ? config.Threads : Environment.ProcessorCount;

                        _logger.LogDebug(
                            "Creating context for '{RepoId}': n_ctx={Ctx}, batch={Batch}, " +
                            "flash={Flash}, embeddings={Emb}, kv_quant={KvQuant}, offload_kqv={Kqv}",
                            repoId, nCtx, nBatch, flashAttn, config.Embeddings, config.KvCacheQuantization, offloadKqv);

                        IntPtr newCtxPtr = _nativeApi.CreateContext(
                            modelPtr,
                            nCtx,
                            nBatch,
                            nThreads,
                            config.Embeddings,
                            flashAttn,
                            kvType,
                            offloadKqv);

                        if (newCtxPtr == IntPtr.Zero)
                            throw new InvalidOperationException($"Failed to create context for '{repoId}'.");

                        textContext = new ModelContext(newCtxPtr, ptr => ReturnContextToPool(repoId, ptr.Handle));
                    }
                    else
                    {
                        throw new InvalidOperationException($"Model '{repoId}' not loaded.");
                    }
                }
                finally
                {
                    initLock.Release();
                }
            }

            _visionCache.TryGetValue(repoId, out var visionContext);
            return new InferenceContext(textContext, visionContext);
        }

        private NativeGgmlType ResolveKvCacheType(string quantization)
        {
            return quantization?.ToUpperInvariant() switch
            {
                "Q8_0" or "Q8_K" => NativeGgmlType.Q8_0,
                "Q5_K" => NativeGgmlType.Q5_K,
                "Q4_K" => NativeGgmlType.Q4_K,
                "Q4_0" => NativeGgmlType.Q4_0,
                "F32" => NativeGgmlType.F32,
                _ => NativeGgmlType.F16
            };
        }

        private void ReturnContextToPool(string repoId, IntPtr ctxPtr)
        {
            if (ctxPtr == IntPtr.Zero) return;
            try
            {
                if (!_modelCache.ContainsKey(repoId))
                {
                    _logger.LogInformation("Model '{RepoId}' is no longer active. Freeing orphaned context.", repoId);
                    _nativeApi.FreeContext(ctxPtr);
                    return;
                }

                _nativeApi.ClearMemory(_nativeApi.GetMemory(ctxPtr), true);
                _pools.GetOrAdd(repoId, _ => new ConcurrentBag<IntPtr>()).Add(ctxPtr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pool context for '{RepoId}'. Freeing memory natively.", repoId);
                _nativeApi.FreeContext(ctxPtr);
            }
        }

        internal Task<ModelWeights> GetWeightsAsync(string repoId, CancellationToken ct = default)
        {
            if (!_modelCache.TryGetValue(repoId, out IntPtr modelPtr))
                throw new KeyNotFoundException($"Weights for '{repoId}' missing.");

            return Task.FromResult(new ModelWeights(modelPtr, isOwned: false, _nativeApi));
        }

        public void UnloadModel(string repoId)
        {
            if (_pools.TryRemove(repoId, out var pool))
            {
                while (pool.TryTake(out IntPtr ctxPtr))
                {
                    _nativeApi.FreeContext(ctxPtr);
                }
            }

            if (_visionCache.TryRemove(repoId, out var visionCtx))
            {
                visionCtx.Dispose();
            }

            if (_modelCache.TryRemove(repoId, out IntPtr modelPtr))
            {
                _nativeApi.FreeModel(modelPtr);
            }

            _configCache.TryRemove(repoId, out _);
            _initLocks.TryRemove(repoId, out _);

            _logger.LogInformation("Model '{RepoId}' was successfully unloaded and memory cleared.", repoId);
        }

        public IEnumerable<ModelRegistryStatus> GetStatus() => _modelCache.Keys.Select(r =>
        {
            _pools.TryGetValue(r, out var p);
            _configCache.TryGetValue(r, out var c);
            return new ModelRegistryStatus(r, true, p?.Count ?? 0, c?.MaxContexts ?? 4, c?.GpuLayerCount ?? 0, c?.Type ?? Domain.Enums.ModelType.Llm);
        });

        public IEnumerable<NativeModelDetails> GetNativeDetails() => _modelCache.Keys.Select(r =>
        {
            _configCache.TryGetValue(r, out var c);
            _pools.TryGetValue(r, out var p);
            return new NativeModelDetails
            {
                RepoId = r,
                ContextSize = c?.ContextSize ?? 2048,
                GpuLayers = c?.GpuLayerCount ?? 0,
                Threads = c?.Threads ?? 4,
                FlashAttention = c?.FlashAttention ?? false,
                IdleContextsCount = p?.Count ?? 0,
                Backend = "auto"
            };
        });

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            foreach (var visionCtx in _visionCache.Values)
                visionCtx.Dispose();

            foreach (var p in _pools.Values)
                while (p.TryTake(out IntPtr ctxPtr))
                    _nativeApi.FreeContext(ctxPtr);

            foreach (var modelPtr in _modelCache.Values)
                _nativeApi.FreeModel(modelPtr);

            _pools.Clear();
            _modelCache.Clear();
            _visionCache.Clear();
            _initLocks.Clear();
            _configCache.Clear();

            lock (_backendLock)
            {
                if (_isBackendInitialized)
                {
                    _nativeApi.BackendFree();
                    _isBackendInitialized = false;
                }
            }
        }
    }
}