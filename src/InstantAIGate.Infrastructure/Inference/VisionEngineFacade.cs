using InstantAIGate.Infrastructure.Inference.layers;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Represents the extracted inference data ready for standard LLM evaluation.
    /// </summary>
    public sealed record ExtractedVisionData(
        IntPtr EmbeddingsPtr,
        NativeMtmdMethods.MtmdDecoderPos[] Positions,
        int TokenCount
    );

    /// <summary>
    /// Encapsulates the multimodal pipeline into validated, isolated stages.
    /// Provides strict assertions for native memory state and prevents silent failures.
    /// </summary>
    public sealed class VisionEngineFacade
    {
        private readonly ILogger<VisionEngineFacade> _logger;

        public VisionEngineFacade(ILogger<VisionEngineFacade> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        /// Allocates memory for the image, initializes the native bitmap, and assigns an ID for KV-cache tracking.
        /// Validates memory bounds and metadata consistency.
        /// </summary>
        /// <param name="rawRgbData">Decoded RGB byte array.</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="hashId">Unique identifier for the image cache.</param>
        /// <returns>A tuple containing the native bitmap pointer and the allocated RGB data pointer.</returns>
        /// <exception cref="InvalidOperationException">Thrown when native validation fails.</exception>
        public (IntPtr BitmapPtr, IntPtr RawRgbPtr) PrepareMediaValidated(byte[] rawRgbData, uint width, uint height, string hashId)
        {
            if (rawRgbData == null || rawRgbData.Length == 0)
                throw new ArgumentException("Image data cannot be null or empty.", nameof(rawRgbData));

            if (string.IsNullOrWhiteSpace(hashId))
                throw new ArgumentException("Hash ID is required for KV caching.", nameof(hashId));

            IntPtr rawRgbPtr = Marshal.AllocHGlobal(rawRgbData.Length);
            Marshal.Copy(rawRgbData, 0, rawRgbPtr, rawRgbData.Length);

            IntPtr bitmapPtr = NativeMtmdMethods.BitmapInit(width, height, rawRgbPtr);
            if (bitmapPtr == IntPtr.Zero)
            {
                Marshal.FreeHGlobal(rawRgbPtr);
                _logger.LogError("NativeMtmdMethods.BitmapInit returned a zero pointer.");
                throw new InvalidOperationException("Failed to initialize native bitmap.");
            }

            NativeMtmdMethods.BitmapSetId(bitmapPtr, hashId);

            uint actualWidth = NativeMtmdMethods.BitmapGetNx(bitmapPtr);
            uint actualHeight = NativeMtmdMethods.BitmapGetNy(bitmapPtr);
            if (actualWidth != width || actualHeight != height)
            {
                _logger.LogError("Bitmap geometry mismatch. Expected: {ExpectedW}x{ExpectedH}, Actual: {ActualW}x{ActualH}", width, height, actualWidth, actualHeight);
                throw new InvalidOperationException("Native bitmap geometry validation failed.");
            }

            nuint actualBytes = NativeMtmdMethods.BitmapGetNBytes(bitmapPtr);
            if (actualBytes != (nuint)rawRgbData.Length)
            {
                _logger.LogError("Bitmap memory size mismatch. Expected: {Expected}, Actual: {Actual}", rawRgbData.Length, actualBytes);
                throw new InvalidOperationException("Native bitmap memory validation failed.");
            }

            IntPtr retrievedIdPtr = NativeMtmdMethods.BitmapGetId(bitmapPtr);
            string? retrievedId = Marshal.PtrToStringAnsi(retrievedIdPtr);
            if (!string.Equals(retrievedId, hashId, StringComparison.Ordinal))
            {
                _logger.LogError("Bitmap ID mismatch. Expected: {Expected}, Actual: {Actual}", hashId, retrievedId);
                throw new InvalidOperationException("Native bitmap ID assignment failed.");
            }

            _logger.LogDebug("Media prepared and validated successfully. ID: {HashId}, Size: {Width}x{Height}", hashId, width, height);

            return (bitmapPtr, rawRgbPtr);
        }

        /// <summary>
        /// Tokenizes the prompt, applying media markers, and generates typed input chunks.
        /// Validates tokenization success and extracts the specific vision chunk pointer.
        /// </summary>
        /// <param name="contextHandle">The active vision context handle.</param>
        /// <param name="prompt">The text prompt containing the media marker.</param>
        /// <param name="bitmapPtr">The validated native bitmap pointer.</param>
        /// <returns>A tuple containing the chunks list pointer and the specific vision chunk pointer.</returns>
        public (IntPtr ChunksPtr, IntPtr VisionChunkPtr) TokenizeAndValidateChunks(IntPtr contextHandle, string prompt, IntPtr bitmapPtr)
        {
            if (contextHandle == IntPtr.Zero) throw new ArgumentException("Context handle cannot be zero.", nameof(contextHandle));
            if (bitmapPtr == IntPtr.Zero) throw new ArgumentException("Bitmap pointer cannot be zero.", nameof(bitmapPtr));

            IntPtr chunksPtr = NativeMtmdMethods.InputChunksInit();
            if (chunksPtr == IntPtr.Zero)
            {
                _logger.LogError("NativeMtmdMethods.InputChunksInit returned a zero pointer.");
                throw new InvalidOperationException("Failed to initialize input chunks container.");
            }

            IntPtr promptPtr = Marshal.StringToHGlobalAnsi(prompt);
            try
            {
                var inputText = new NativeMtmdMethods.MtmdInputText
                {
                    Text = promptPtr,
                    AddSpecial = true,
                    ParseSpecial = true
                };

                var bitmaps = new[] { bitmapPtr };
                int tokenizationResult = NativeMtmdMethods.Tokenize(contextHandle, chunksPtr, ref inputText, bitmaps, 1);

                if (tokenizationResult != 0)
                {
                    _logger.LogError("NativeMtmdMethods.Tokenize failed with code: {Code}", tokenizationResult);
                    throw new InvalidOperationException($"Tokenization failed with error code {tokenizationResult}.");
                }

                nuint size = NativeMtmdMethods.InputChunksSize(chunksPtr);
                if (size == 0)
                {
                    _logger.LogError("Tokenization succeeded but produced 0 chunks.");
                    throw new InvalidOperationException("Tokenization produced empty chunks.");
                }

                int textChunks = 0;
                int imageChunks = 0;
                IntPtr visionChunkPtr = IntPtr.Zero;

                for (nuint i = 0; i < size; i++)
                {
                    IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                    var type = NativeMtmdMethods.InputChunkGetType(chunk);

                    if (type == NativeMtmdMethods.MtmdInputChunkType.Text)
                    {
                        textChunks++;
                    }
                    else if (type == NativeMtmdMethods.MtmdInputChunkType.Image)
                    {
                        imageChunks++;
                        visionChunkPtr = chunk;
                        nuint tokens = NativeMtmdMethods.InputChunkGetNTokens(chunk);
                        _logger.LogDebug("Identified image chunk at index {Index} requiring {Tokens} tokens.", i, tokens);
                    }
                }

                _logger.LogInformation("Tokenization complete. Total Chunks: {Total}, Text: {Text}, Image: {Image}", size, textChunks, imageChunks);

                if (visionChunkPtr == IntPtr.Zero)
                {
                    _logger.LogError("No image chunk found in the tokenized output. Ensure the prompt contains the correct media marker.");
                    throw new InvalidOperationException("Missing vision chunk after tokenization.");
                }

                return (chunksPtr, visionChunkPtr);
            }
            finally
            {
                if (promptPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(promptPtr);
                }
            }
        }

        /// <summary>
        /// Aggregates chunks into a batch and encodes them through the vision model.
        /// Asserts the state of batch creation and encoding execution.
        /// </summary>
        /// <param name="contextHandle">The active vision context handle.</param>
        /// <param name="chunksPtr">The validated chunks list pointer.</param>
        /// <returns>The pointer to the encoded batch.</returns>
        public IntPtr EncodeBatchSafe(IntPtr contextHandle, IntPtr chunksPtr)
        {
            if (contextHandle == IntPtr.Zero) throw new ArgumentException("Context handle cannot be zero.", nameof(contextHandle));
            if (chunksPtr == IntPtr.Zero) throw new ArgumentException("Chunks pointer cannot be zero.", nameof(chunksPtr));

            IntPtr batchPtr = NativeMtmdMethods.BatchInit(contextHandle);
            if (batchPtr == IntPtr.Zero)
            {
                _logger.LogError("NativeMtmdMethods.BatchInit returned a zero pointer.");
                throw new InvalidOperationException("Failed to initialize processing batch.");
            }

            nuint size = NativeMtmdMethods.InputChunksSize(chunksPtr);
            for (nuint i = 0; i < size; i++)
            {
                IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                int addResult = NativeMtmdMethods.BatchAddChunk(batchPtr, chunk);
                if (addResult != 0)
                {
                    _logger.LogError("BatchAddChunk failed for chunk {Index} with code: {Code}", i, addResult);
                    throw new InvalidOperationException($"Failed to add chunk {i} to batch. Code: {addResult}");
                }
            }

            _logger.LogDebug("Starting BatchEncode for {Count} chunks.", size);

            int encodeResult = NativeMtmdMethods.BatchEncode(batchPtr);
            if (encodeResult != 0)
            {
                _logger.LogError("NativeMtmdMethods.BatchEncode failed with code: {Code}. Possible VRAM exhaustion or backend failure.", encodeResult);
                throw new InvalidOperationException($"Batch encoding failed with error code {encodeResult}.");
            }

            _logger.LogInformation("Batch encoding completed successfully.");

            return batchPtr;
        }

        /// <summary>
        /// Extracts the generated embeddings and M-RoPE positional data required for LLM evaluation.
        /// </summary>
        /// <param name="batchPtr">The successfully encoded batch pointer.</param>
        /// <param name="visionChunkPtr">The specific pointer to the vision chunk.</param>
        /// <returns>An immutable record containing the embeddings pointer and positional data array.</returns>
        public ExtractedVisionData ExtractInferenceData(IntPtr batchPtr, IntPtr visionChunkPtr)
        {
            if (batchPtr == IntPtr.Zero) throw new ArgumentException("Batch pointer cannot be zero.", nameof(batchPtr));
            if (visionChunkPtr == IntPtr.Zero) throw new ArgumentException("Vision chunk pointer cannot be zero.", nameof(visionChunkPtr));

            IntPtr embeddingsPtr = NativeMtmdMethods.BatchGetOutputEmbd(batchPtr, visionChunkPtr);
            if (embeddingsPtr == IntPtr.Zero)
            {
                _logger.LogError("NativeMtmdMethods.BatchGetOutputEmbd returned a zero pointer.");
                throw new InvalidOperationException("Failed to extract embeddings from batch.");
            }

            nuint tokenCount = NativeMtmdMethods.InputChunkGetNTokens(visionChunkPtr);
            int posCount = NativeMtmdMethods.InputChunkGetNPos(visionChunkPtr);

            if ((nuint)posCount != tokenCount)
            {
                _logger.LogWarning("Position count ({PosCount}) differs from token count ({TokenCount}). This may be expected for specific M-RoPE architectures.", posCount, tokenCount);
            }

            IntPtr imageTokensPtr = NativeMtmdMethods.InputChunkGetTokensImage(visionChunkPtr);
            if (imageTokensPtr == IntPtr.Zero)
            {
                _logger.LogError("NativeMtmdMethods.InputChunkGetTokensImage returned a zero pointer.");
                throw new InvalidOperationException("Failed to extract image tokens pointer.");
            }

            var positions = new NativeMtmdMethods.MtmdDecoderPos[posCount];
            for (nuint i = 0; i < (nuint)posCount; i++)
            {
                positions[i] = NativeMtmdMethods.ImageTokensGetDecoderPos(imageTokensPtr, 0, i);
            }

            _logger.LogDebug("Successfully extracted inference data. Tokens: {Tokens}, Positions extracted: {PosCount}", tokenCount, posCount);

            return new ExtractedVisionData(embeddingsPtr, positions, (int)tokenCount);
        }
    }
}