// File: src/InstantAIGate.Core/Services/Inference/ModelProvider.cs
namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native; // <-- New Opaque Handles
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ModelProvider : IModelProvider, IDisposable
{
    private readonly ILogger<ModelProvider> _logger;
    private readonly IBackendFacade _backendFacade;
    private readonly IVisionFacade _visionFacade;

    // RULE OF OPAQUE HANDLES: IntPtr replaced with Marker Interfaces
    private readonly ConcurrentDictionary<string, IModelHandle> _modelCache = new();
    private readonly ConcurrentDictionary<string, VisionContext> _visionCache = new();
    private readonly ConcurrentDictionary<string, ModelSettings> _configCache = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<IContextHandle>> _pools = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _initLocks = new();

    private static bool _isBackendInitialized;
    private static readonly object _backendLock = new();
    private static BackendLogCallback? _logCallback;
    private static ILogger<ModelProvider>? _staticLogger;

    private static bool _isStderrRedirected;
    private static readonly object _stderrLock = new();

    public ModelProvider(
        ILogger<ModelProvider> logger,
        IBackendFacade backendFacade,
        IVisionFacade visionFacade)
    {
        _logger = logger;
        _backendFacade = backendFacade;
        _visionFacade = visionFacade;
        _staticLogger = logger;

        RedirectStderr();
    }

    #region Logging & Bootstrapping

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
            _backendFacade.SetLogCallback(_logCallback);
        }
    }

    private static void LlamaLogHandler(int level, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _staticLogger == null) return;

        string cleanMessage = message.TrimEnd('\n', '\r');
        if (string.IsNullOrWhiteSpace(cleanMessage)) return;

        // Mapped from native ints to prevent Core from knowing about Native enums
        switch (level)
        {
            case 3: _staticLogger.LogError("[Native] {Message}", cleanMessage); break; // Error
            case 2: _staticLogger.LogWarning("[Native] {Message}", cleanMessage); break; // Warn
            case 4: _staticLogger.LogDebug("[Native] {Message}", cleanMessage); break; // Debug
            default: _staticLogger.LogInformation("[Native] {Message}", cleanMessage); break; // Info
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
            if (value is null) return;
            _buffer.Append(value);
            if (value.Contains('\n')) FlushLine();
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

        // NO MORE DIRECTORY SCANNING HERE. The paths are strictly resolved by IModelLocator.

        var repoId = config.RepoId;
        var initLock = _initLocks.GetOrAdd(repoId, _ => new SemaphoreSlim(1, 1));

        await initLock.WaitAsync(ct);
        try
        {
            if (_modelCache.ContainsKey(repoId)) return;

            lock (_backendLock)
            {
                if (!_isBackendInitialized)
                {
                    _logger.LogInformation("Initializing backends via Facade...");
                    SetupLlamaLogging();
                    _backendFacade.LoadAllBackends();
                    _backendFacade.BackendInit();
                    _isBackendInitialized = true;
                }
            }

            _logger.LogInformation("Delegating Model Load to Native Facade for '{RepoId}'...", repoId);

            IModelHandle modelHandle = _backendFacade.LoadModel(config);

            if (modelHandle == null)
                throw new InvalidOperationException($"Native engine returned null handle for '{repoId}'.");

            if (_modelCache.TryAdd(repoId, modelHandle))
            {
                _configCache.TryAdd(repoId, config);
                _logger.LogInformation("Model '{RepoId}' loaded successfully.", repoId);

                // Config now definitively has the projector path if VisionSupport is true
                if (config.VisionSupport && !string.IsNullOrEmpty(config.ProjectorPath))
                {
                    var visionContext = _visionFacade.InitializeContext(config.ProjectorPath, modelHandle);
                    _visionCache.TryAdd(repoId, visionContext);
                }
            }
            else
            {
                _backendFacade.FreeModel(modelHandle);
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

        if (_pools.TryGetValue(repoId, out var pool) && pool.TryTake(out IContextHandle? ctxHandle))
        {
            _backendFacade.ClearContextMemory(ctxHandle, true);
            textContext = new ModelContext(ctxHandle, ptr => ReturnContextToPool(repoId, ptr));
        }
        else
        {
            var initLock = _initLocks.GetOrAdd(repoId, _ => new SemaphoreSlim(1, 1));
            await initLock.WaitAsync(ct);
            try
            {
                if (_pools.TryGetValue(repoId, out pool) && pool.TryTake(out ctxHandle))
                {
                    _backendFacade.ClearContextMemory(ctxHandle, true);
                    textContext = new ModelContext(ctxHandle, ptr => ReturnContextToPool(repoId, ptr));
                }
                else if (_modelCache.TryGetValue(repoId, out IModelHandle? modelHandle) && _configCache.TryGetValue(repoId, out var config))
                {
                    // Core no longer knows about 'flashAttn' enums or 'ggml_type'. It just passes the config.
                    IContextHandle newCtxHandle = _backendFacade.CreateContext(modelHandle, config);

                    if (newCtxHandle == null)
                        throw new InvalidOperationException($"Failed to create context for '{repoId}'.");

                    textContext = new ModelContext(newCtxHandle, ptr => ReturnContextToPool(repoId, ptr));
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

        _visionCache.TryGetValue(repoId, out var masterVisionContext);
        VisionContext? requestVisionContext = masterVisionContext != null
            ? new VisionContext(masterVisionContext.Handle, _ => { })
            : null;

        return new InferenceContext(textContext, requestVisionContext);
    }

    private void ReturnContextToPool(string repoId, IContextHandle ctxHandle)
    {
        if (ctxHandle == null) return;

        try
        {
            if (!_modelCache.ContainsKey(repoId))
            {
                _logger.LogInformation("Model '{RepoId}' is no longer active. Freeing orphaned context.", repoId);
                _backendFacade.FreeContext(ctxHandle);
                return;
            }

            _backendFacade.ClearContextMemory(ctxHandle, true);
            _pools.GetOrAdd(repoId, _ => new ConcurrentBag<IContextHandle>()).Add(ctxHandle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pool context for '{RepoId}'. Freeing natively.", repoId);
            _backendFacade.FreeContext(ctxHandle);
        }
    }

    public Task<ModelWeights> GetWeightsAsync(string repoId, CancellationToken ct = default)
    {
        if (!_modelCache.TryGetValue(repoId, out IModelHandle? modelHandle))
            throw new KeyNotFoundException($"Weights for '{repoId}' missing.");

        // isOwned: false ensures the caller cannot dispose the cached native model
        return Task.FromResult(new ModelWeights(modelHandle, isOwned: false, _backendFacade));
    }

    public void UnloadModel(string repoId)
    {
        if (_pools.TryRemove(repoId, out var pool))
        {
            while (pool.TryTake(out IContextHandle? ctxHandle))
            {
                _backendFacade.FreeContext(ctxHandle);
            }
        }

        if (_visionCache.TryRemove(repoId, out var visionCtx))
        {
            visionCtx.Dispose();
        }

        if (_modelCache.TryRemove(repoId, out IModelHandle? modelHandle))
        {
            _backendFacade.FreeModel(modelHandle);
        }

        _configCache.TryRemove(repoId, out _);
        _initLocks.TryRemove(repoId, out _);
        _logger.LogInformation("Model '{RepoId}' was successfully unloaded and memory cleared.", repoId);
    }

    public IEnumerable<ModelRegistryStatus> GetStatus() => _modelCache.Keys.Select(r =>
    {
        _pools.TryGetValue(r, out var p);
        _configCache.TryGetValue(r, out var c);
        return new ModelRegistryStatus(
            r, true, p?.Count ?? 0, c?.MaxContexts ?? 4, c?.GpuLayerCount ?? 0, c?.Type ?? ModelType.Audio);
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
            Backend = "abstracted" // Abstraction prevents knowing backend specifically here
        };
    });

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var visionCtx in _visionCache.Values) visionCtx.Dispose();
        foreach (var p in _pools.Values)
            while (p.TryTake(out IContextHandle? ctxHandle))
                _backendFacade.FreeContext(ctxHandle);

        foreach (var modelHandle in _modelCache.Values)
            _backendFacade.FreeModel(modelHandle);

        _pools.Clear();
        _modelCache.Clear();
        _visionCache.Clear();
        _initLocks.Clear();
        _configCache.Clear();

        lock (_backendLock)
        {
            if (_isBackendInitialized)
            {
                _backendFacade.BackendFree();
                _isBackendInitialized = false;
            }
        }
    }
}