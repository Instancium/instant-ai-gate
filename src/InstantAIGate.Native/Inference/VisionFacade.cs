namespace InstantAIGate.Native.Inference;

using System;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;

/// <summary>
/// Implementation of IVisionFacade wrapping mtmd multimodal bindings.
/// </summary>
public class VisionFacade : IVisionFacade
{
    /// <summary>
    /// Initializes a vision context for multimodal inference.
    /// </summary>
    /// <param name="projectorPath">Path to the vision projector file.</param>
    /// <param name="modelHandle">Handle to the base text model.</param>
    /// <returns>Vision context wrapper.</returns>
    public VisionContext InitializeContext(string projectorPath, IntPtr modelHandle)
    {
        if (string.IsNullOrWhiteSpace(projectorPath))
        {
            throw new ArgumentException("Projector path is required for vision context.", nameof(projectorPath));
        }

        if (modelHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Model handle is invalid.", nameof(modelHandle));
        }

        // Initialize with default parameters or configure explicitly
        var ctxParams = MtmdNative.mtmd_context_params_default();

        // Example: ctxParams.NThreads = Environment.ProcessorCount;

        IntPtr visionCtxPtr = MtmdNative.mtmd_init_from_file(projectorPath, modelHandle, in ctxParams);

        if (visionCtxPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to initialize vision context from '{projectorPath}'.");
        }

        return new VisionContext(visionCtxPtr, ptr => MtmdNative.mtmd_free(ptr));
    }
}