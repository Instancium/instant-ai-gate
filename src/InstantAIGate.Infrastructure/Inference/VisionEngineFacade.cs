using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference
{
    public sealed record ExtractedVisionData(
        IntPtr EmbeddingsPtr,
        NativeMtmdMethods.MtmdDecoderPos[] Positions,
        int TokenCount,
        bool RequiresNonCausalAttention
    );

    public sealed record PromptSegment(int[]? TextTokens, ExtractedVisionData? VisionData);

    public sealed class VisionEngineFacade
    {
        private readonly ILogger<VisionEngineFacade> _logger;

        public VisionEngineFacade(ILogger<VisionEngineFacade> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public VisionContext InitializeVision(string projectorPath, IntPtr textModelHandle, bool useGpu = true, int batchMaxTokens = 4096)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
                throw new ArgumentException("Projector path cannot be empty.", nameof(projectorPath));
            if (textModelHandle == IntPtr.Zero)
                throw new ArgumentException("Text model handle cannot be zero.", nameof(textModelHandle));

            var ctxParams = NativeMtmdMethods.GetDefaultContextParams();
            ctxParams.UseGpu = useGpu;
            ctxParams.BatchMaxTokens = batchMaxTokens;
            ctxParams.ImageMinTokens = 1024;
            ctxParams.Warmup = true;

            var handle = NativeMtmdMethods.InitFromFile(projectorPath, textModelHandle, ctxParams);
            if (handle == IntPtr.Zero)
            {
                _logger.LogError("Failed to initialize vision context from projector: {Path}. UseGpu: {UseGpu}", projectorPath, useGpu);
                throw new InvalidOperationException("Native mtmd initialization failed.");
            }

            _logger.LogInformation("Vision context initialized successfully. Projector: {Path}, GPU: {UseGpu}, BatchTokens: {Tokens}",
                projectorPath, useGpu, batchMaxTokens);

            return new VisionContext(handle);
        }

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
            int mediaChunksAdded = 0;

            for (nuint i = 0; i < size; i++)
            {
                IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                var chunkType = NativeMtmdMethods.InputChunkGetType(chunk);

                if (chunkType != NativeMtmdMethods.MtmdInputChunkType.Image && chunkType != NativeMtmdMethods.MtmdInputChunkType.Audio)
                {
                    _logger.LogDebug("Skipping chunk {Index} of type {Type} during media batch allocation.", i, chunkType);
                    continue;
                }

                int addResult = NativeMtmdMethods.BatchAddChunk(batchPtr, chunk);
                if (addResult != 0)
                {
                    _logger.LogError("BatchAddChunk failed for media chunk {Index} with code: {Code}", i, addResult);
                    throw new InvalidOperationException($"Failed to add media chunk {i} to batch. Code: {addResult}");
                }

                mediaChunksAdded++;
            }

            if (mediaChunksAdded == 0)
            {
                _logger.LogWarning("No media chunks found to encode. Bypassing BatchEncode.");
                return batchPtr;
            }

            _logger.LogDebug("Starting BatchEncode for {Count} media chunks.", mediaChunksAdded);
            int encodeResult = NativeMtmdMethods.BatchEncode(batchPtr);
            if (encodeResult != 0)
            {
                _logger.LogError("NativeMtmdMethods.BatchEncode failed with code: {Code}. Possible VRAM exhaustion or backend failure.", encodeResult);
                throw new InvalidOperationException($"Batch encoding failed with error code {encodeResult}.");
            }

            _logger.LogInformation("Batch encoding completed successfully.");
            return batchPtr;
        }

        public ExtractedVisionData ExtractInferenceData(IntPtr contextHandle, IntPtr batchPtr, IntPtr visionChunkPtr)
        {
            if (contextHandle == IntPtr.Zero) throw new ArgumentException("Context handle cannot be zero.", nameof(contextHandle));
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
                _logger.LogWarning("Position count ({PosCount}) differs from token count ({TokenCount}). Expected for specific M-RoPE architectures.", posCount, tokenCount);
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

            bool nonCausal = NativeMtmdMethods.DecodeUseNonCausal(contextHandle, visionChunkPtr);

            _logger.LogDebug("Extracted data. Tokens: {Tokens}, Positions: {PosCount}, Non-Causal: {NonCausal}", tokenCount, posCount, nonCausal);

            return new ExtractedVisionData(embeddingsPtr, positions, (int)tokenCount, nonCausal);
        }

        public List<PromptSegment> ParseChunksIntoSegments(IntPtr contextHandle, IntPtr chunksPtr, IntPtr visionBatchPtr)
        {
            var segments = new List<PromptSegment>();
            nuint chunksCount = NativeMtmdMethods.InputChunksSize(chunksPtr);

            for (nuint i = 0; i < chunksCount; i++)
            {
                IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                var chunkType = NativeMtmdMethods.InputChunkGetType(chunk);

                if (chunkType == NativeMtmdMethods.MtmdInputChunkType.Text)
                {
                    IntPtr textTokensPtr = NativeMtmdMethods.InputChunkGetTokensText(chunk, out nuint nTokens);
                    if (nTokens > 0)
                    {
                        int[] tokenArray = new int[nTokens];
                        Marshal.Copy(textTokensPtr, tokenArray, 0, (int)nTokens);
                        segments.Add(new PromptSegment(tokenArray, null));
                    }
                }
                else if (chunkType == NativeMtmdMethods.MtmdInputChunkType.Image)
                {
                    ExtractedVisionData visionData = ExtractInferenceData(contextHandle, visionBatchPtr, chunk);
                    segments.Add(new PromptSegment(null, visionData));
                }
            }
            return segments;
        }

        public void FreeVisionResources(IntPtr visionBatchPtr, IntPtr chunksPtr, IntPtr bitmapPtr, IntPtr rawRgbPtr)
        {
            if (visionBatchPtr != IntPtr.Zero) NativeMtmdMethods.BatchFree(visionBatchPtr);
            if (chunksPtr != IntPtr.Zero) NativeMtmdMethods.InputChunksFree(chunksPtr);
            if (bitmapPtr != IntPtr.Zero) NativeMtmdMethods.BitmapFree(bitmapPtr);
            if (rawRgbPtr != IntPtr.Zero) Marshal.FreeHGlobal(rawRgbPtr);
        }
    }
}