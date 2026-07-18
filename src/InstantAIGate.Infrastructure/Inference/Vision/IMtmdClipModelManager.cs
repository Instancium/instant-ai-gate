namespace InstantAIGate.Infrastructure.Inference.Vision
{
    public interface IMtmdClipModelManager
    {
        /// <summary>
        /// Acquires thread-safe access to a multimodal context, initializing it if necessary.
        /// </summary>
        /// <param name="projectorPath">Path to the mmproj file.</param>
        /// <param name="textModelPtr">Pointer to the initialized text llama_model.</param>
        /// <param name="useGpu">Whether to offload to GPU.</param>
        Task<MtmdClipContext> AcquireContextAsync(string projectorPath, IntPtr textModelPtr, bool useGpu = true, CancellationToken ct = default);

        Task UnloadModelAsync(string projectorPath, CancellationToken ct = default);
    }

}