using System;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Abstraction layer for the MTMD (Multi-Modal) CLIP API.
    /// Manages the loading of vision/audio projectors and configuration extraction.
    /// </summary>
    public interface IMtmdClipApi
    {
        /// <summary>
        /// Evaluates the capabilities of a given MTMD model file.
        /// </summary>
        /// <param name="filePath">Path to the MTMD model file.</param>
        /// <returns>Capabilities indicating vision or audio support.</returns>
        NativeMethods.clip_cap GetCapabilities(string filePath);

        /// <summary>
        /// Initializes the MTMD clip context for vision and/or audio processing.
        /// </summary>
        /// <param name="filePath">Path to the MTMD model file.</param>
        /// <param name="useGpu">Determines if the GPU should be utilized.</param>
        /// <returns>Initialization result containing context pointers.</returns>
        NativeMethods.clip_init_result Initialize(string filePath, bool useGpu);

        /// <summary>
        /// Releases all resources associated with the specified clip context.
        /// </summary>
        /// <param name="context">Pointer to the clip context.</param>
        void FreeContext(IntPtr context);

        /// <summary>
        /// Retrieves the embedding dimension size for the multimodel projector.
        /// </summary>
        /// <param name="context">Pointer to the clip context.</param>
        /// <returns>The embedding dimension size.</returns>
        int GetProjectorEmbeddingSize(IntPtr context);

        /// <summary>
        /// Initializes an empty unmanaged f32 image structure.
        /// </summary>
        /// <returns>Pointer to the allocated clip_image_f32 structure.</returns>
        IntPtr InitializeImageF32();

        /// <summary>
        /// Frees the unmanaged f32 image structure.
        /// </summary>
        /// <param name="imageContext">Pointer to the clip_image_f32 structure.</param>
        void FreeImageF32(IntPtr imageContext);
    }
}