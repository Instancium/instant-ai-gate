// File: src/InstantAIGate.Native/Inference/BackendFacade.cs
namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.Handles;
using System;
using System.Runtime.InteropServices;

public class BackendFacade : IBackendFacade
{
    public void LoadAllBackends() => LlamaNative.llama_backend_init();
    public void BackendInit() => LlamaNative.llama_backend_init();
    public void BackendFree() => LlamaNative.llama_backend_free();
    public bool SupportsGpuOffload() => LlamaNative.llama_supports_gpu_offload();
    private static GgmlLogCallback? _nativeLogCallback;
    public IModelHandle LoadModel(ModelSettings settings, string modelPath)
    {
        var modelParams = LlamaNative.llama_model_default_params();
        modelParams.NGpuLayers = settings.GpuLayerCount;
        modelParams.MainGpu = settings.MainGPU;
        modelParams.LoadMode = !settings.UseMemoryLock ? LlamaLoadMode.MMap : LlamaLoadMode.None;
        modelParams.SplitMode = settings.GpuLayerCount > 0 ? LlamaSplitMode.Layer : LlamaSplitMode.None;

        IntPtr ptr = LlamaNative.llama_model_load_from_file(modelPath, in modelParams, (nuint)modelPath.Length);

        return ptr != IntPtr.Zero ? new LlamaModelHandle(ptr) : throw new InvalidOperationException("Failed to load model natively.");
    }

    public IContextHandle CreateContext(IModelHandle modelHandle, ModelSettings settings)
    {
        if (modelHandle is not LlamaModelHandle nativeHandle)
            throw new ArgumentException("Invalid model handle type.", nameof(modelHandle));

        var ctxParams = LlamaNative.llama_context_default_params();
        ctxParams.NCtx = settings.ContextSize > 0 ? (uint)settings.ContextSize : 2048;
        ctxParams.NBatch = settings.BatchSize > 0 ? (uint)settings.BatchSize : 512;
        ctxParams.NThreads = settings.Threads > 0 ? settings.Threads : Environment.ProcessorCount;
        ctxParams.NThreadsBatch = ctxParams.NThreads;
        ctxParams.Embeddings = settings.Embeddings;
        ctxParams.OffloadKqv = settings.GpuLayerCount > 0;
        ctxParams.FlashAttnType = settings.FlashAttention ? LlamaFlashAttnType.Enabled : LlamaFlashAttnType.Disabled;

        var ggmlType = settings.KvCacheQuantization?.ToUpperInvariant() switch
        {
            "Q8_0" or "Q8_K" => GgmlType.Q8_0,
            "Q4_K" => GgmlType.Q4_K,
            "Q5_K" => GgmlType.Q5_K,
            "F32" => GgmlType.F32,
            "Q4_0" => GgmlType.Q4_0,
            _ => GgmlType.F16
        };

        ctxParams.TypeK = ggmlType;
        ctxParams.TypeV = ggmlType;

        IntPtr ptr = LlamaNative.llama_init_from_model(nativeHandle.Pointer, in ctxParams);
        return ptr != IntPtr.Zero ? new LlamaContextHandle(ptr) : throw new InvalidOperationException("Failed to create context natively.");
    }

    public void FreeModel(IModelHandle modelHandle)
    {
        if (modelHandle is LlamaModelHandle nativeHandle && nativeHandle.Pointer != IntPtr.Zero)
            LlamaNative.llama_model_free(nativeHandle.Pointer);
    }

    public void FreeContext(IContextHandle contextHandle)
    {
        if (contextHandle is LlamaContextHandle nativeHandle && nativeHandle.Pointer != IntPtr.Zero)
            LlamaNative.llama_free(nativeHandle.Pointer);
    }

    public void ClearContextMemory(IContextHandle contextHandle, bool clearKvCache)
    {
        if (contextHandle is LlamaContextHandle nativeHandle && nativeHandle.Pointer != IntPtr.Zero)
        {
            IntPtr memPtr = LlamaNative.llama_get_memory(nativeHandle.Pointer);
            if (memPtr != IntPtr.Zero)
            {
                LlamaNative.llama_memory_clear(memPtr, clearKvCache);
            }
        }
    }


    public void SetLogCallback(BackendLogCallback callback)
    {
        _nativeLogCallback = (level, text, _) =>
        {
            if (text == IntPtr.Zero) return;

            try
            {
                int coreLevel = level switch
                {
                    GgmlLogLevel.Error => 3,
                    GgmlLogLevel.Warn => 2,
                    GgmlLogLevel.Debug => 4,
                    _ => 1
                };
                string message = Marshal.PtrToStringUTF8(text) ?? string.Empty;
                callback.Invoke(coreLevel, message);
            }
            catch
            {

            }
        };

        LlamaNative.llama_log_set(_nativeLogCallback, IntPtr.Zero);
    }
}