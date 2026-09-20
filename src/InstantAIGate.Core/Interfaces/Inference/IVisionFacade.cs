// File: src/InstantAIGate.Core/Interfaces/Inference/IVisionFacade.cs
namespace InstantAIGate.Core.Interfaces.Inference
{
    using InstantAIGate.Core.Dtos.Inference;
    using InstantAIGate.Core.Interfaces.Native;

    public interface IVisionFacade
    {
        VisionContext InitializeContext(string projectorPath, IModelHandle modelHandle);
    }
}