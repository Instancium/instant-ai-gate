// File: src/InstantAIGate.Core/Interfaces/Inference/IBackendFacade.cs
namespace InstantAIGate.Core.Interfaces.Inference
{
    using System;
    using InstantAIGate.Core.Interfaces.Native;
    using InstantAIGate.Core.Dtos.Config;

    public delegate void BackendLogCallback(int level, string message);

    public interface IBackendFacade
    {
        void LoadAllBackends();
        void BackendInit();
        void BackendFree();
        bool SupportsGpuOffload();

        IModelHandle LoadModel(ModelSettings settings);
        IContextHandle CreateContext(IModelHandle modelHandle, ModelSettings settings);

        void FreeModel(IModelHandle modelHandle);
        void FreeContext(IContextHandle contextHandle);
        void ClearContextMemory(IContextHandle contextHandle, bool clearKvCache);

        void SetLogCallback(BackendLogCallback callback);
    }
}