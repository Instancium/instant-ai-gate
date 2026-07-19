using InstantAIGate.Infrastructure.Inference.layers;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Orchestrates multimodal processing workflows using the underlying libmtmd library.
    /// Handles resource lifecycle and sequence execution for vision embeddings.
    /// </summary>
    public sealed class NativeVisionApi
    {
        private readonly ILogger<NativeVisionApi> _logger;

        public NativeVisionApi(ILogger<NativeVisionApi> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Initializes the multimodal context by binding the projection model to the text model.
        /// </summary>
        /// <param name="projectorPath">Path to the .mmproj file.</param>
        /// <param name="textModelHandle">Handle of the loaded native llama model.</param>
        /// <returns>A managed context wrapping the native mtmd handle.</returns>
        /// <exception cref="ArgumentException">Thrown when paths or handles are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when native initialization fails.</exception>
        public VisionContext InitializeVision(string projectorPath, IntPtr textModelHandle)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
                throw new ArgumentException("Projector path cannot be empty.", nameof(projectorPath));

            if (textModelHandle == IntPtr.Zero)
                throw new ArgumentException("Text model handle cannot be zero.", nameof(textModelHandle));

            var ctxParams = NativeMtmdMethods.GetDefaultContextParams();
            var handle = NativeMtmdMethods.InitFromFile(projectorPath, textModelHandle, ctxParams);

            if (handle == IntPtr.Zero)
            {
                _logger.LogError("Failed to initialize vision context from projector: {Path}", projectorPath);
                throw new InvalidOperationException("Native mtmd initialization failed.");
            }

            _logger.LogInformation("Vision context initialized successfully using projector: {Path}", projectorPath);
            return new VisionContext(handle);
        }

        /// <summary>
        /// Processes an image and text prompt through the multimodal pipeline to generate embeddings.
        /// </summary>
        /// <param name="visionContext">The active vision context.</param>
        /// <param name="prompt">The text prompt containing the media marker.</param>
        /// <param name="rawRgbData">Decoded RGB image data.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <returns>Pointer to the computed embeddings to be passed to the LLM evaluation.</returns>
        public IntPtr ProcessMediaPrompt(VisionContext visionContext, string prompt, byte[] rawRgbData, uint width, uint height)
        {
            IntPtr promptPtr = IntPtr.Zero;
            IntPtr bitmapPtr = IntPtr.Zero;
            IntPtr chunksPtr = IntPtr.Zero;
            IntPtr batchPtr = IntPtr.Zero;
            IntPtr rawRgbPtr = IntPtr.Zero;

            try
            {
                rawRgbPtr = Marshal.AllocHGlobal(rawRgbData.Length);
                Marshal.Copy(rawRgbData, 0, rawRgbPtr, rawRgbData.Length);

                bitmapPtr = NativeMtmdMethods.BitmapInit(width, height, rawRgbPtr);
                if (bitmapPtr == IntPtr.Zero) throw new InvalidOperationException("Failed to initialize mtmd bitmap.");

                promptPtr = Marshal.StringToHGlobalAnsi(prompt);
                var inputText = new NativeMtmdMethods.MtmdInputText
                {
                    Text = promptPtr,
                    AddSpecial = true,
                    ParseSpecial = true
                };

                chunksPtr = NativeMtmdMethods.InputChunksInit();

                var bitmaps = new[] { bitmapPtr };
                int tokenizationResult = NativeMtmdMethods.Tokenize(visionContext.Handle, chunksPtr, ref inputText, bitmaps, 1);

                if (tokenizationResult != 0)
                {
                    _logger.LogError("Tokenization failed for media prompt. Code: {Code}", tokenizationResult);
                    throw new InvalidOperationException("Vision tokenization failed.");
                }

                batchPtr = NativeMtmdMethods.BatchInit(visionContext.Handle);
                nuint chunksCount = NativeMtmdMethods.InputChunksSize(chunksPtr);

                for (nuint i = 0; i < chunksCount; i++)
                {
                    IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                    NativeMtmdMethods.BatchAddChunk(batchPtr, chunk);
                }

                int encodeResult = NativeMtmdMethods.BatchEncode(batchPtr);
                if (encodeResult != 0)
                {
                    _logger.LogError("Batch encoding failed for media prompt. Code: {Code}", encodeResult);
                    throw new InvalidOperationException("Vision batch encoding failed.");
                }

                return NativeMtmdMethods.GetOutputEmbd(visionContext.Handle);
            }
            finally
            {
                if (promptPtr != IntPtr.Zero) Marshal.FreeHGlobal(promptPtr);
                if (rawRgbPtr != IntPtr.Zero) Marshal.FreeHGlobal(rawRgbPtr);
                if (bitmapPtr != IntPtr.Zero) NativeMtmdMethods.BitmapFree(bitmapPtr);
                if (chunksPtr != IntPtr.Zero) NativeMtmdMethods.InputChunksFree(chunksPtr);
                if (batchPtr != IntPtr.Zero) NativeMtmdMethods.BatchFree(batchPtr);
            }
        }
    }
}