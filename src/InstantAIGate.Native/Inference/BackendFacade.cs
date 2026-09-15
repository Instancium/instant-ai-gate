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
    /// <returns>True if GPU offload is supported.</returns>
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
        var nativeSplitMode = splitMode switch
        {
            BackendSplitMode.Layer => LlamaSplitMode.Layer,
            BackendSplitMode.Row => LlamaSplitMode.Row,
            _ => LlamaSplitMode.None
        };


        var modelParams = new LlamaModelParams
        {
            NGpuLayers = gpuLayers,
            MainGpu = mainGpu,
            SplitMode = nativeSplitMode,
            LoadMode = useMlock ? LlamaLoadMode.MLock : (useMmap ? LlamaLoadMode.MMap : LlamaLoadMode.None),
            LazyMode = LlamaLazyMode.Auto,
            Devices = IntPtr.Zero,
            TensorBuftOverrides = IntPtr.Zero,
            TensorSplit = IntPtr.Zero,
            ProgressCallback = IntPtr.Zero,
            ProgressCallbackUserData = IntPtr.Zero,
            KvOverrides = IntPtr.Zero,
            VocabOnly = false,
            CheckTensors = false,
            UseExtraBufts = false,
            NoHost = false,
            NoAlloc = false,
            LoadMtp = false
        };

 
        var pathLen = (nuint)System.Text.Encoding.UTF8.GetByteCount(path);

        return LlamaNative.llama_model_load_from_file(path, in modelParams, pathLen);
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
        var params_ = LlamaNative.llama_context_default_params();

        params_.NCtx = nCtx;
        params_.NBatch = nBatch;
        params_.NUbatch = nBatch; // Usually n_ubatch == n_batch for standard inference
        params_.NThreads = nThreads;
        params_.NThreadsBatch = nThreads;
        params_.Embeddings = embeddings;
        params_.OffloadKqv = offloadKqv;

        params_.FlashAttnType = flashAttn switch
        {
            BackendFlashAttentionType.Enabled => LlamaFlashAttnType.Enabled,
            BackendFlashAttentionType.Disabled => LlamaFlashAttnType.Disabled,
            _ => LlamaFlashAttnType.Auto
        };

        // Map abstract KV cache type to native GGML type
        var ggmlType = kvType switch
        {
            BackendKvCacheType.Q8_0 => GgmlType.Q8_0,
            BackendKvCacheType.Q5_K => GgmlType.Q5_K,
            BackendKvCacheType.Q4_K => GgmlType.Q4_K,
            BackendKvCacheType.Q4_0 => GgmlType.Q4_0,
            BackendKvCacheType.F32 => GgmlType.F32,
            _ => GgmlType.F16 // Default fallback
        };

        params_.TypeK = ggmlType;
        params_.TypeV = ggmlType;

        return LlamaNative.llama_init_from_model(modelPtr, in params_);
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
    /// Gets the memory context for clearing.
    /// </summary>
    public IntPtr GetMemory(IntPtr ctxPtr)
    {
        return LlamaNative.llama_get_memory(ctxPtr);
    }

    /// <summary>
    /// Clears the memory context.
    /// </summary>
    public void ClearMemory(IntPtr memoryPtr, bool clearKvCache)
    {
        if (clearKvCache && memoryPtr != IntPtr.Zero)
        {
            LlamaNative.llama_memory_clear(memoryPtr, data: true);
        }
    }

    /// <summary>
    /// Sets the logging callback for native operations.
    /// </summary>
    public void SetLogCallback(BackendLogCallback callback)
    {
        GgmlLogCallback nativeCallback = (level, text, _) =>
        {
            var coreLevel = level switch
            {
                GgmlLogLevel.Error => BackendLogLevel.Error,
                GgmlLogLevel.Warn => BackendLogLevel.Warning,
                GgmlLogLevel.Debug => BackendLogLevel.Debug,
                _ => BackendLogLevel.Info
            };

            callback.Invoke(coreLevel, Marshal.PtrToStringUTF8(text) ?? string.Empty);
        };

        LlamaNative.llama_log_set(nativeCallback, IntPtr.Zero);
    }
}