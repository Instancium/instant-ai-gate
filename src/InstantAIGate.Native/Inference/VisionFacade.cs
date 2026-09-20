// File: src/InstantAIGate.Native/Inference/VisionFacade.cs
namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.Handles;
using System;

public class VisionFacade : IVisionFacade
{
    public VisionContext InitializeContext(string projectorPath, IModelHandle modelHandle)
    {
        if (string.IsNullOrWhiteSpace(projectorPath))
            throw new ArgumentException("Projector path is required.", nameof(projectorPath));

        if (modelHandle is not LlamaModelHandle nativeHandle)
            throw new ArgumentException("Invalid model handle type.", nameof(modelHandle));

        var ctxParams = MtmdNative.mtmd_context_params_default();
        IntPtr visionCtxPtr = MtmdNative.mtmd_init_from_file(projectorPath, nativeHandle.Pointer, in ctxParams);

        if (visionCtxPtr == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to initialize vision context from '{projectorPath}'.");

        var visionHandle = new MtmdVisionHandle(visionCtxPtr);
        return new VisionContext(visionHandle, handle =>
        {
            if (handle is MtmdVisionHandle vh && vh.Pointer != IntPtr.Zero)
                MtmdNative.mtmd_free(vh.Pointer);
        });
    }
}