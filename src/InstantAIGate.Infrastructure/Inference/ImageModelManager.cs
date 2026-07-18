using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.Inference.Vision;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;


namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Manages the lifecycle and concurrent access to MTMD vision models.
    /// </summary>
    public sealed class ImageModelManager : IMtmdClipModelManager, IDisposable
    {
        private readonly INativeMtmdApi _mtmdApi;
        private readonly ILogger<ImageModelManager> _logger;

        private readonly ConcurrentDictionary<string, IntPtr> _activeContexts = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _accessLocks = new();
        private readonly SemaphoreSlim _globalLock = new(1, 1);

        public ImageModelManager(INativeMtmdApi mtmdApi, ILogger<ImageModelManager> logger)
        {
            _mtmdApi = mtmdApi;
            _logger = logger;
        }

        public async Task<MtmdClipContext> AcquireContextAsync(string projectorPath, IntPtr textModelPtr, bool useGpu = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
                throw new ArgumentException("Projector model path cannot be null or empty.", nameof(projectorPath));

            if (textModelPtr == IntPtr.Zero)
                throw new ArgumentException("Text model pointer cannot be null.", nameof(textModelPtr));

            // 1. Thread-safe initialization
            if (!_activeContexts.ContainsKey(projectorPath))
            {
                await _globalLock.WaitAsync(ct);
                try
                {
                    if (!_activeContexts.ContainsKey(projectorPath))
                    {
                        if (!File.Exists(projectorPath))
                            throw new FileNotFoundException("MTMD Projector model file not found.", projectorPath);

                        _logger.LogInformation("Loading MTMD vision model from '{Path}' (GPU: {UseGpu})", projectorPath, useGpu);

                        // Pass both the projector path and the text model pointer to the native API
                        IntPtr ctxPtr = _mtmdApi.InitializeContext(projectorPath, textModelPtr, useGpu);

                        if (ctxPtr == IntPtr.Zero)
                            throw new InvalidOperationException($"Failed to initialize MTMD context for '{projectorPath}'.");

                        _activeContexts.TryAdd(projectorPath, ctxPtr);
                        _accessLocks.TryAdd(projectorPath, new SemaphoreSlim(1, 1));

                        _logger.LogInformation("✅ MTMD vision model loaded successfully.");
                    }
                }
                finally
                {
                    _globalLock.Release();
                }
            }

            // 2. Lock for exclusive inference execution
            var semaphore = _accessLocks[projectorPath];
            await semaphore.WaitAsync(ct);

            try
            {
                IntPtr handle = _activeContexts[projectorPath];
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
                    _logger.LogInformation("Unloading MTMD vision model '{Path}'.", projectorPath);
                    _mtmdApi.FreeContext(ctxPtr);

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
                try { _mtmdApi.FreeContext(ctx); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error freeing MTMD context during shutdown."); }
            }

            foreach (var sem in _accessLocks.Values) sem.Dispose();

            _activeContexts.Clear();
            _accessLocks.Clear();
        }
    }
}
