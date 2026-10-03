// File: src/InstantAIGate.Core/Interfaces/Inference/IBackendFacade.cs
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

        /// <summary>
        /// Validates whether the active native context supports physical KV-cache shifting (<c>llama_memory_can_shift</c>).
        /// Returns <c>false</c> if FlashAttention, unified KV, or GPU offload profiles prevent memory displacement.
        /// </summary>
        bool CanShiftContextMemory(IContextHandle contextHandle);

        /// <summary>
        /// Removes token KV-cache entries within the half-open interval [<paramref name="p0"/>, <paramref name="p1"/>).
        /// Used deterministically by Rollback (<c>p1 = -1</c>) across all hardware profiles.
        /// </summary>
        bool RemoveContextMemoryRange(IContextHandle contextHandle, int seqId, int p0, int p1);

        /// <summary>
        /// Shifts sequence positions by <paramref name="delta"/> for tokens in [<paramref name="p0"/>, <paramref name="p1"/>).
        /// WARNING: Must only be invoked if <see cref="CanShiftContextMemory"/> evaluates to <c>true</c>.
        /// </summary>
        void ShiftContextMemoryRange(IContextHandle contextHandle, int seqId, int p0, int p1, int delta);

        int GetModelLayerCount(IModelHandle modelHandle);
    }
}