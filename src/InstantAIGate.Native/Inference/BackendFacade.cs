namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Inference.Native;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Implementation of IBackendFacade mapping abstract Core types to native llama.cpp P/Invoke calls.
/// </summary>
public class BackendFacade : IBackendFacade
{

    /// <summary>
    /// Loads all available native backends.
    /// </summary>
    public void LoadAllBackends()
    {
        LlamaNative.llama_backend_init();
    }

    /// <summary>
    /// Initializes the backend system.
    /// </summary>
    public void BackendInit()
    {
        LlamaNative.llama_backend_init();
    }

    /// <summary>
    /// Frees all backend resources.
    /// </summary>
    public void BackendFree()
    {
        LlamaNative.llama_backend_free();
    }

    /// <summary>
    /// Checks if GPU offload is supported.
    /// </summary>
    public bool SupportsGpuOffload()
    {
        return LlamaNative.llama_supports_gpu_offload();
    }

    /// <summary>
    /// Loads a model from the specified path.
    /// </summary>
    public IntPtr LoadModel(
        string path,
        int gpuLayers,
        int mainGpu,
        bool useMlock,
        bool useMmap,
        BackendSplitMode splitMode)
    {
        var modelParams = LlamaNative.llama_model_default_params();
        modelParams.NGpuLayers = gpuLayers;
        modelParams.MainGpu = mainGpu;
        // modelParams.UseMlock = useMlock;
        modelParams.LoadMode = useMmap ? LlamaLoadMode.MMap : LlamaLoadMode.None;

        modelParams.SplitMode = splitMode switch
        {
            BackendSplitMode.Layer => LlamaSplitMode.Layer,
            BackendSplitMode.Row => LlamaSplitMode.Row,
            _ => LlamaSplitMode.None
        };

        return LlamaNative.llama_model_load_from_file(path, in modelParams, (nuint)path.Length);
    }

    /// <summary>
    /// Creates an inference context for the specified model.
    /// </summary>
    public IntPtr CreateContext(
        IntPtr modelPtr,
        uint nCtx,
        uint nBatch,
        int nThreads,
        bool embeddings,
        BackendFlashAttentionType flashAttn,
        BackendKvCacheType kvType,
        bool offloadKqv)
    {
        var ctxParams = LlamaNative.llama_context_default_params();
        ctxParams.NCtx = nCtx;
        ctxParams.NBatch = nBatch;
        ctxParams.NUbatch = nBatch;
        ctxParams.NThreads = nThreads;
        ctxParams.NThreadsBatch = nThreads;
        ctxParams.Embeddings = embeddings;
        ctxParams.OffloadKqv = offloadKqv;

        ctxParams.FlashAttnType = flashAttn switch
        {
            BackendFlashAttentionType.Enabled => LlamaFlashAttnType.Enabled,
            BackendFlashAttentionType.Disabled => LlamaFlashAttnType.Disabled,
            _ => LlamaFlashAttnType.Auto
        };

        var ggmlType = kvType switch
        {
            BackendKvCacheType.Q8_0 => GgmlType.Q8_0,
            BackendKvCacheType.Q4_K => GgmlType.Q4_K,
            BackendKvCacheType.Q5_K => GgmlType.Q5_K,
            BackendKvCacheType.F32 => GgmlType.F32,
            BackendKvCacheType.Q4_0 => GgmlType.Q4_0,
            _ => GgmlType.F16
        };

        ctxParams.TypeK = ggmlType;
        ctxParams.TypeV = ggmlType;

        return LlamaNative.llama_init_from_model(modelPtr, in ctxParams);
    }

    /// <summary>
    /// Frees a model from memory.
    /// </summary>
    public void FreeModel(IntPtr modelPtr)
    {
        if (modelPtr != IntPtr.Zero)
        {
            LlamaNative.llama_model_free(modelPtr);
        }
    }

    /// <summary>
    /// Frees a context from memory.
    /// </summary>
    public void FreeContext(IntPtr ctxPtr)
    {
        if (ctxPtr != IntPtr.Zero)
        {
            LlamaNative.llama_free(ctxPtr);
        }
    }

    /// <summary>
    /// Gets the memory handle from context.
    /// </summary>
    public IntPtr GetMemory(IntPtr ctxPtr)
    {
        return LlamaNative.llama_get_memory(ctxPtr);
    }

    /// <summary>
    /// Clears the memory contents.
    /// </summary>
    public void ClearMemory(IntPtr memoryPtr, bool clearKvCache)
    {
        if (memoryPtr != IntPtr.Zero)
        {
            LlamaNative.llama_memory_clear(memoryPtr, clearKvCache);
        }
    }


    // Keeping the reference to prevent Garbage Collection
    private GgmlLogCallback? _nativeLogCallback;

    /// <summary>
    /// Sets the logging callback for native operations.
    /// </summary>

    public void SetLogCallback(BackendLogCallback callback)
    {
        _nativeLogCallback = (level, text, _) =>
        {
            if (text == IntPtr.Zero) return;

            // 'level' is now natively recognized as GgmlLogLevel
            var coreLevel = level switch
            {
                GgmlLogLevel.Error => BackendLogLevel.Error,
                GgmlLogLevel.Warn => BackendLogLevel.Warning,
                GgmlLogLevel.Debug => BackendLogLevel.Debug,
                GgmlLogLevel.Info => BackendLogLevel.Info,
                GgmlLogLevel.Cont => BackendLogLevel.Info, // Map continuation to Info
                _ => BackendLogLevel.Info
            };

            string message = Marshal.PtrToStringUTF8(text) ?? string.Empty;
            callback.Invoke(coreLevel, message);
        };

        LlamaNative.llama_log_set(_nativeLogCallback, IntPtr.Zero);
    }
}