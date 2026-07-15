using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;


namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Manages the lifecycle and concurrent access to MTMD (CLIP) vision models.
    /// </summary>
    public sealed class MtmdClipModelManager : IMtmdClipModelManager, IDisposable
    {
        private readonly INativeMtmdApi _clipApi;
        private readonly ILogger<MtmdClipModelManager> _logger;

        // Cache of loaded CLIP model context pointers (Key: File Path)
        private readonly ConcurrentDictionary<string, IntPtr> _activeContexts = new();

        // Semaphores for inference synchronization (1 thread per 1 CLIP model)
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _accessLocks = new();

        // Global lock for thread-safe model loading
        private readonly SemaphoreSlim _globalLock = new(1, 1);

        public MtmdClipModelManager(INativeMtmdApi clipApi, ILogger<MtmdClipModelManager> logger)
        {
            _clipApi = clipApi;
            _logger = logger;
        }

        public async Task<MtmdClipContext> AcquireContextAsync(string projectorPath, bool useGpu = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
                throw new ArgumentException("Projector model path cannot be null or empty.", nameof(projectorPath));
      
            // Step 1: Thread-safe model loading if not already loaded
            if (!_activeContexts.ContainsKey(projectorPath))
            {
                await _globalLock.WaitAsync(ct);
                try
                {
                    if (!_activeContexts.ContainsKey(projectorPath))
                    {
                        if (!File.Exists(projectorPath))
                            throw new FileNotFoundException("CLIP Projector model file not found.", projectorPath);

                        _logger.LogInformation("Loading MTMD CLIP model from '{Path}' (GPU Offload: {UseGpu})", projectorPath, useGpu);

                        // Initialize context via native API
                        var initResult = _clipApi.Initialize(projectorPath, useGpu);

                        IntPtr ctxPtr = GetPointerFromResult(initResult);

                        if (ctxPtr == IntPtr.Zero)
                            throw new InvalidOperationException($"Failed to initialize CLIP context for '{projectorPath}'.");

                        _activeContexts.TryAdd(projectorPath, ctxPtr);
                        _accessLocks.TryAdd(projectorPath, new SemaphoreSlim(1, 1)); // Strict single-thread access

                        _logger.LogInformation("✅ MTMD CLIP model loaded successfully.");
                    }
                }
                finally
                {
                    _globalLock.Release();
                }
            }

            // Step 2: Acquire lock for exclusive inference execution on this model
            var semaphore = _accessLocks[projectorPath];
            await semaphore.WaitAsync(ct);

            try
            {
                IntPtr handle = _activeContexts[projectorPath];

                // Return disposable wrapper to release semaphore automatically
                return new MtmdClipContext(handle, () => semaphore.Release());
            }
            catch
            {
                semaphore.Release();
                throw;
            }
        }

        public async Task UnloadModelAsync(string projectorPath, CancellationToken ct = default)
        {
            await _globalLock.WaitAsync(ct);
            try
            {
                if (_activeContexts.TryRemove(projectorPath, out var ctxPtr))
                {
                    _logger.LogInformation("Unloading MTMD CLIP model '{Path}' and freeing memory.", projectorPath);
                    _clipApi.FreeContext(ctxPtr);

                    if (_accessLocks.TryRemove(projectorPath, out var sem))
                    {
                        sem.Dispose();
                    }
                }
            }
            finally
            {
                _globalLock.Release();
            }
        }

        public void Dispose()
        {
            _globalLock.Dispose();

            foreach (var ctx in _activeContexts.Values)
            {
                try
                {
                    _clipApi.FreeContext(ctx);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while freeing MTMD CLIP context during application shutdown.");
                }
            }

            foreach (var sem in _accessLocks.Values)
            {
                sem.Dispose();
            }

            _activeContexts.Clear();
            _accessLocks.Clear();
        }

        /// <summary>
        /// Extracts the vision context pointer (ctx_v) from the native initialization result.
        /// </summary>
        private IntPtr GetPointerFromResult(NativeMethods.clip_init_result result)
        {
            // We are implementing the Vision pipeline, so we need the vision context.
            return result.ctx_v;
        }
    }
}