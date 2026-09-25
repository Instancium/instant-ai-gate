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
    }
}