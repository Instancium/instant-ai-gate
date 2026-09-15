namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Inference.Native;

/// <summary>
/// Provides abstraction over native backend operations.
/// </summary>
public interface IBackendFacade
{
    /// <summary>
    /// Loads all available backends.
    /// </summary>
    void LoadAllBackends();

    /// <summary>
    /// Initializes the backend system.
    /// </summary>
    void BackendInit();

    /// <summary>
    /// Frees all backend resources.
    /// </summary>
    void BackendFree();

    /// <summary>
    /// Checks if GPU offload is supported.
    /// </summary>
    /// <returns>True if GPU offload is supported.</returns>
    bool SupportsGpuOffload();

    /// <summary>
    /// Loads a model from the specified path.
    /// </summary>
    /// <param name="path">Path to the model file.</param>
    /// <param name="gpuLayers">Number of layers to offload to GPU.</param>
    /// <param name="mainGpu">Main GPU device index.</param>
    /// <param name="useMlock">Whether to lock model memory.</param>
    /// <param name="useMmap">Whether to use memory mapping.</param>
    /// <param name="splitMode">Layer split mode.</param>
    /// <returns>Model handle pointer.</returns>
    IntPtr LoadModel(
        string path,
        int gpuLayers,
        int mainGpu,
        bool useMlock,
        bool useMmap,
        BackendSplitMode splitMode);

    /// <summary>
    /// Creates an inference context for the specified model.
    /// </summary>
    /// <param name="modelPtr">Model handle pointer.</param>
    /// <param name="nCtx">Context size.</param>
    /// <param name="nBatch">Batch size.</param>
    /// <param name="nThreads">Number of threads.</param>
    /// <param name="embeddings">Whether to enable embeddings mode.</param>
    /// <param name="flashAttn">Flash attention type.</param>
    /// <param name="kvType">KV cache type.</param>
    /// <param name="offloadKqv">Whether to offload KQV to GPU.</param>
    /// <returns>Context handle pointer.</returns>
    IntPtr CreateContext(
        IntPtr modelPtr,
        uint nCtx,
        uint nBatch,
        int nThreads,
        bool embeddings,
        BackendFlashAttentionType flashAttn,
        BackendKvCacheType kvType,
        bool offloadKqv);

    /// <summary>
    /// Frees a model from memory.
    /// </summary>
    /// <param name="modelPtr">Model handle pointer.</param>
    void FreeModel(IntPtr modelPtr);

    /// <summary>
    /// Frees a context from memory.
    /// </summary>
    /// <param name="ctxPtr">Context handle pointer.</param>
    void FreeContext(IntPtr ctxPtr);

    /// <summary>
    /// Gets the memory context for clearing.
    /// </summary>
    /// <param name="ctxPtr">Context handle pointer.</param>
    /// <returns>Memory context pointer.</returns>
    IntPtr GetMemory(IntPtr ctxPtr);

    /// <summary>
    /// Clears the memory context.
    /// </summary>
    /// <param name="memoryPtr">Memory context pointer.</param>
    /// <param name="clearKvCache">Whether to clear KV cache.</param>
    void ClearMemory(IntPtr memoryPtr, bool clearKvCache);

    /// <summary>
    /// Sets the logging callback for native operations.
    /// </summary>
    /// <param name="callback">Logging callback delegate.</param>
    void SetLogCallback(BackendLogCallback callback);
}