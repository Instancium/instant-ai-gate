namespace InstantAIGate.Core.Interfaces.Inference
{
    using InstantAIGate.Core.Dtos.Config;
    using InstantAIGate.Core.Interfaces.Native;

    public delegate void BackendLogCallback(int level, string message);

    public interface IBackendFacade
    {
        void LoadAllBackends();
        void BackendInit();
        void BackendFree();
        bool SupportsGpuOffload();
        IModelHandle LoadModel(ModelSettings settings, string modelPath);
        IContextHandle CreateContext(IModelHandle modelHandle, ModelSettings settings);
        void FreeModel(IModelHandle modelHandle);
        void FreeContext(IContextHandle contextHandle);
        void ClearContextMemory(IContextHandle contextHandle, bool clearKvCache);
        void SetLogCallback(BackendLogCallback callback);
        bool CanShiftContextMemory(IContextHandle contextHandle);
        bool RemoveContextMemoryRange(IContextHandle contextHandle, int seqId, int p0, int p1);
        void ShiftContextMemoryRange(IContextHandle contextHandle, int seqId, int p0, int p1, int delta);
        int GetModelLayerCount(IModelHandle modelHandle);

        /// <summary>
        /// Reads the sequence-level embedding vector produced after a successful llama_decode pass.
        /// Returns null when the underlying context was not created with the Embeddings flag enabled
        /// or when the native backend does not expose embeddings for the given sequence.
        /// </summary>
        /// <param name="contextHandle">Context handle wrapping the decoded llama context.</param>
        /// <param name="seqId">Sequence identifier, typically 0 for single-sequence embedding requests.</param>
        /// <returns>A managed float array containing the embedding, or null when unavailable.</returns>
        float[]? GetEmbeddings(IContextHandle contextHandle, int seqId);
    }
}