using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Facades
{
    /// <summary>
    /// Core interface for loading models and managing inference contexts within ModelProvider.
    /// Excludes generation, sampling, and decoding operations.
    /// </summary>
    public interface IBackendFacade
    {
        void LoadAllBackends();
        void BackendInit();
        void BackendFree();
        bool SupportsGpuOffload();
        void SetLogCallback(NativeLogCallback callback);
        IntPtr LoadModel(string path, int gpuLayers, int mainGpu, bool useMlock, bool useMmap, NativeLlamaSplitMode splitMode);
        void FreeModel(IntPtr model);
        IntPtr CreateContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, bool embeddings, NativeLlamaFlashAttnType flashAttn, NativeGgmlType kvType, bool offloadKqv);
        void FreeContext(IntPtr context);
        IntPtr GetMemory(IntPtr context);
        void ClearMemory(IntPtr memory, bool clear);
    }

    /// <summary>
    /// Lightweight facade handling memory-safe P/Invoke operations for backend and model initialization.
    /// </summary>
    public sealed class BackendFacade : IBackendFacade
    {
        private NativeLlamaMethods.GgmlLogCallback? _nativeCallback;
        private readonly ILogger<BackendFacade> _logger;

        public BackendFacade(ILogger<BackendFacade> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void LoadAllBackends()
        {
            try
            {
                NativeLlamaMethods.GgmlBackendLoadAll();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load GGML backends.");
            }

            try
            {
                NativeLlamaMethods.LlamaBackendLoadAll();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load LLaMA backends.");
            }
        }

        public void BackendInit() => NativeLlamaMethods.LlamaBackendInit();

        public void BackendFree() => NativeLlamaMethods.LlamaBackendFree();

        public bool SupportsGpuOffload() => NativeLlamaMethods.LlamaSupportsGpuOffload();

        public void SetLogCallback(NativeLogCallback callback)
        {
            _nativeCallback = (NativeLlamaMethods.GgmlLogLevel level, IntPtr text, IntPtr userData) =>
            {
                if (text == IntPtr.Zero) return;

                string? message = Marshal.PtrToStringAnsi(text)?.TrimEnd('\n', '\r');
                if (string.IsNullOrEmpty(message)) return;

                callback((NativeGgmlLogLevel)level, message);
            };

            try { NativeLlamaMethods.GgmlLogSet(_nativeCallback, IntPtr.Zero); } catch { }
            try { NativeLlamaMethods.LlamaLogSet(_nativeCallback, IntPtr.Zero); } catch { }
        }

        public IntPtr LoadModel(string path, int gpuLayers, int mainGpu, bool useMlock, bool useMmap, NativeLlamaSplitMode splitMode)
        {
            var p = NativeLlamaMethods.LlamaModelDefaultParams();
            p.NGpuLayers = gpuLayers;
            p.MainGpu = mainGpu;
            p.UseMlock = useMlock;
            p.UseMmap = useMmap;
            p.SplitMode = (NativeLlamaMethods.LlamaSplitMode)splitMode;

            IntPtr modelHandle = NativeLlamaMethods.LlamaModelLoadFromFile(path, p);

            if (modelHandle == IntPtr.Zero)
            {
                _logger.LogError("NativeLlamaMethods.LlamaModelLoadFromFile returned a zero pointer for path: {Path}", path);
                throw new InvalidOperationException("Failed to load native LLM weights into memory.");
            }

            return modelHandle;
        }

        public void FreeModel(IntPtr model)
        {
            if (model != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaModelFree(model);
            }
        }

        public IntPtr CreateContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, bool embeddings, NativeLlamaFlashAttnType flashAttn, NativeGgmlType kvType, bool offloadKqv)
        {
            if (model == IntPtr.Zero) throw new ArgumentException("Model handle cannot be zero.", nameof(model));

            var p = NativeLlamaMethods.LlamaContextDefaultParams();
            p.NCtx = nCtx;
            p.NBatch = nBatch;
            p.NUBatch = nBatch;
            p.NThreads = nThreads;
            p.NThreadsBatch = nThreads;
            p.Embeddings = embeddings;
            p.FlashAttnType = (NativeLlamaMethods.LlamaFlashAttnType)flashAttn;
            p.TypeK = (NativeLlamaMethods.GgmlType)kvType;
            p.TypeV = (NativeLlamaMethods.GgmlType)kvType;
            p.OffloadKqv = offloadKqv;

            IntPtr ctxHandle = NativeLlamaMethods.LlamaInitFromModel(model, p);

            if (ctxHandle == IntPtr.Zero)
            {
                _logger.LogError("NativeLlamaMethods.LlamaInitFromModel returned a zero pointer. Check VRAM capacity.");
                throw new InvalidOperationException("Failed to initialize inference context.");
            }

            return ctxHandle;
        }

        public void FreeContext(IntPtr context)
        {
            if (context != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaFree(context);
            }
        }

        public IntPtr GetMemory(IntPtr context) => NativeLlamaMethods.LlamaGetMemory(context);

        public void ClearMemory(IntPtr memory, bool clear) => NativeLlamaMethods.LlamaMemoryClear(memory, clear);
    }
}