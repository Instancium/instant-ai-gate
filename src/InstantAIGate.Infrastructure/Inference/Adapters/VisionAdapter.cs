using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    /// <summary>
    /// Orchestrates multimodal inference by synchronizing the VisionEngineFacade and LlamaEngineFacade.
    /// Handles media parsing, dynamic batch assembly, and streaming evaluation.
    /// </summary>
    public sealed class VisionAdapter
    {
        private readonly VisionEngineFacade _visionFacade;
        private readonly LlamaEngineFacade _llamaFacade;
        private readonly ILogger<VisionAdapter> _logger;

        public VisionAdapter(
            VisionEngineFacade visionFacade,
            LlamaEngineFacade llamaFacade,
            ILogger<VisionAdapter> logger)
        {
            _visionFacade = visionFacade ?? throw new ArgumentNullException(nameof(visionFacade));
            _llamaFacade = llamaFacade ?? throw new ArgumentNullException(nameof(llamaFacade));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Generates a streaming response for a multimodal prompt.
        /// </summary>
        public async IAsyncEnumerable<string> StreamVisionResponseAsync(
            VisionContext visionContext,
            IntPtr llamaContext,
            IntPtr llamaVocab,
            string prompt,
            byte[] imageBytes,
            uint width,
            uint height,
            string hashId,
            int maxTokens,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            IntPtr bitmapPtr = IntPtr.Zero;
            IntPtr rawRgbPtr = IntPtr.Zero;
            IntPtr chunksPtr = IntPtr.Zero;
            IntPtr visionBatchPtr = IntPtr.Zero;
            IntPtr samplerChain = IntPtr.Zero;

            try
            {
                // 1. Prepare Media via Vision Facade
                (bitmapPtr, rawRgbPtr) = _visionFacade.PrepareMediaValidated(imageBytes, width, height, hashId);

                // 2. Tokenize prompt into mixed Text/Image chunks
                var (newChunksPtr, _) = _visionFacade.TokenizeAndValidateChunks(visionContext.Handle, prompt, bitmapPtr);
                chunksPtr = newChunksPtr;

                // 3. Pre-compute vision embeddings in a dedicated batch
                visionBatchPtr = _visionFacade.EncodeBatchSafe(visionContext.Handle, chunksPtr);

                int currentSequencePosition = 0;
                nuint chunksCount = NativeMtmdMethods.InputChunksSize(chunksPtr);

                // 4. Sequential Evaluation Loop (Feed LLM in exact prompt order)
                for (nuint i = 0; i < chunksCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunksPtr, i);
                    var type = NativeMtmdMethods.InputChunkGetType(chunk);

                    if (type == NativeMtmdMethods.MtmdInputChunkType.Text)
                    {
                        IntPtr textTokensPtr = NativeMtmdMethods.InputChunkGetTokensText(chunk, out nuint nTokens);
                        if (nTokens > 0)
                        {
                            int[] tokenArray = new int[nTokens];
                            Marshal.Copy(textTokensPtr, tokenArray, 0, (int)nTokens);

                            _logger.LogDebug("Decoding text chunk {Index} with {Count} tokens.", i, nTokens);
                            int evaluated = _llamaFacade.DecodeTextBatchSafe(llamaContext, tokenArray, currentSequencePosition);
                            currentSequencePosition += evaluated;
                        }
                    }
                    else if (type == NativeMtmdMethods.MtmdInputChunkType.Image)
                    {
                        ExtractedVisionData visionData = _visionFacade.ExtractInferenceData(visionContext.Handle, visionBatchPtr, chunk);

                        _logger.LogDebug("Decoding vision chunk {Index} with {Count} embeddings.", i, visionData.TokenCount);
                        int evaluated = _llamaFacade.DecodeVisionBatchSafe(llamaContext, visionData, currentSequencePosition);
                        currentSequencePosition += evaluated;
                    }
                }

                // 5. Initialize Sampler with Repetition Penalties
                var samplerParams = NativeLlamaMethods.LlamaSamplerChainDefaultParams();
                samplerChain = NativeLlamaMethods.LlamaSamplerChainInit(samplerParams);

                // Штраф за повторяемость (penalty_last_n = 64, repeat = 1.1f, freq = 0.1f, present = 0.1f)
                IntPtr repetitionSampler = NativeLlamaMethods.LlamaSamplerInitPenalties(64, 1.1f, 0.1f, 0.1f);
                if (repetitionSampler != IntPtr.Zero)
                {
                    NativeLlamaMethods.LlamaSamplerChainAdd(samplerChain, repetitionSampler);
                }

                NativeLlamaMethods.LlamaSamplerChainAdd(samplerChain, NativeLlamaMethods.LlamaSamplerInitTopK(40));
                NativeLlamaMethods.LlamaSamplerChainAdd(samplerChain, NativeLlamaMethods.LlamaSamplerInitTopP(0.95f, 1));
                NativeLlamaMethods.LlamaSamplerChainAdd(samplerChain, NativeLlamaMethods.LlamaSamplerInitTemp(0.7f));

                uint seed = (uint)Random.Shared.Next();
                NativeLlamaMethods.LlamaSamplerChainAdd(samplerChain, NativeLlamaMethods.LlamaSamplerInitDist(seed));

                int generatedTokens = 0;
                int eosToken = NativeLlamaMethods.LlamaVocabEos(llamaVocab);
                int lastTokenId = -1;
                int repetitionCount = 0;

                // Защита от зацикливания строки
                string lastPieces = string.Empty;

                // 6. Generation Loop
                while (generatedTokens < maxTokens)
                {
                    ct.ThrowIfCancellationRequested();

                    int nextToken = _llamaFacade.SampleTokenSafe(samplerChain, llamaContext, -1);

                    if (nextToken == eosToken || nextToken < 0)
                    {
                        break;
                    }

                    // Защита от зацикливания одного и того же токена
                    if (nextToken == lastTokenId)
                    {
                        repetitionCount++;
                        if (repetitionCount > 3) // Если токен повторился 4 раза подряд — принудительно стоп
                        {
                            break;
                        }
                    }
                    else
                    {
                        repetitionCount = 0;
                        lastTokenId = nextToken;
                    }

                    _llamaFacade.AcceptTokenSafe(samplerChain, nextToken);
                    generatedTokens++;

                    string piece = _llamaFacade.TokenToTextSafe(llamaVocab, nextToken);
                    yield return piece;

                    int[] nextTokenArray = { nextToken };
                    _llamaFacade.DecodeTextBatchSafe(llamaContext, nextTokenArray, currentSequencePosition);
                    currentSequencePosition++;

                    await Task.Yield();
                }
            }
            finally
            {
                // Strict unmanaged memory cleanup
                if (samplerChain != IntPtr.Zero) NativeLlamaMethods.LlamaSamplerFree(samplerChain);
                if (visionBatchPtr != IntPtr.Zero) NativeMtmdMethods.BatchFree(visionBatchPtr);
                if (chunksPtr != IntPtr.Zero) NativeMtmdMethods.InputChunksFree(chunksPtr);
                if (bitmapPtr != IntPtr.Zero) NativeMtmdMethods.BitmapFree(bitmapPtr);
                if (rawRgbPtr != IntPtr.Zero) Marshal.FreeHGlobal(rawRgbPtr);
            }
        }
    }
}