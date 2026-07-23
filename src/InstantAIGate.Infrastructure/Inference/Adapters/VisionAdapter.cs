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
            using var resources = new InferenceResources();

            var (bitmapPtr, rawRgbPtr) = _visionFacade.PrepareMediaValidated(imageBytes, width, height, hashId);
            resources.BitmapPtr = bitmapPtr;
            resources.RawRgbPtr = rawRgbPtr;

            var (newChunksPtr, _) = _visionFacade.TokenizeAndValidateChunks(visionContext.Handle, prompt, resources.BitmapPtr);
            resources.ChunksPtr = newChunksPtr;

            resources.VisionBatchPtr = _visionFacade.EncodeBatchSafe(visionContext.Handle, resources.ChunksPtr);

            int currentSequencePosition = 0;
            nuint chunksCount = NativeMtmdMethods.InputChunksSize(resources.ChunksPtr);

            for (nuint i = 0; i < chunksCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                IntPtr chunk = NativeMtmdMethods.InputChunksGet(resources.ChunksPtr, i);
                var chunkType = NativeMtmdMethods.InputChunkGetType(chunk);

                if (chunkType == NativeMtmdMethods.MtmdInputChunkType.Text)
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
                else if (chunkType == NativeMtmdMethods.MtmdInputChunkType.Image)
                {
                    ExtractedVisionData visionData = _visionFacade.ExtractInferenceData(visionContext.Handle, resources.VisionBatchPtr, chunk);

                    _logger.LogDebug("Decoding vision chunk {Index} with {Count} embeddings.", i, visionData.TokenCount);
                    int evaluated = _llamaFacade.DecodeVisionBatchSafe(llamaContext, visionData, currentSequencePosition);
                    currentSequencePosition += evaluated;
                }
            }

            var samplerParams = NativeLlamaMethods.LlamaSamplerChainDefaultParams();
            resources.SamplerChain = NativeLlamaMethods.LlamaSamplerChainInit(samplerParams);

            // Configures repetition penalties to prevent text generation loops.
            IntPtr repetitionSampler = NativeLlamaMethods.LlamaSamplerInitPenalties(64, 1.1f, 0.1f, 0.1f);
            if (repetitionSampler != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaSamplerChainAdd(resources.SamplerChain, repetitionSampler);
            }

            NativeLlamaMethods.LlamaSamplerChainAdd(resources.SamplerChain, NativeLlamaMethods.LlamaSamplerInitTopK(40));
            NativeLlamaMethods.LlamaSamplerChainAdd(resources.SamplerChain, NativeLlamaMethods.LlamaSamplerInitTopP(0.95f, 1));
            NativeLlamaMethods.LlamaSamplerChainAdd(resources.SamplerChain, NativeLlamaMethods.LlamaSamplerInitTemp(0.7f));

            uint seed = (uint)Random.Shared.Next();
            NativeLlamaMethods.LlamaSamplerChainAdd(resources.SamplerChain, NativeLlamaMethods.LlamaSamplerInitDist(seed));

            int generatedTokens = 0;
            int eosToken = NativeLlamaMethods.LlamaVocabEos(llamaVocab);
            int lastTokenId = -1;
            int repetitionCount = 0;

            while (generatedTokens < maxTokens)
            {
                ct.ThrowIfCancellationRequested();

                int nextToken = _llamaFacade.SampleTokenSafe(resources.SamplerChain, llamaContext, -1);

                if (nextToken == eosToken || nextToken < 0)
                {
                    break;
                }

                // Breaks execution to prevent infinite repeating token loops.
                if (nextToken == lastTokenId)
                {
                    repetitionCount++;
                    if (repetitionCount > 3)
                    {
                        break;
                    }
                }
                else
                {
                    repetitionCount = 0;
                    lastTokenId = nextToken;
                }

                _llamaFacade.AcceptTokenSafe(resources.SamplerChain, nextToken);
                generatedTokens++;

                string piece = _llamaFacade.TokenToTextSafe(llamaVocab, nextToken);
                yield return piece;

                int[] nextTokenArray = { nextToken };
                _llamaFacade.DecodeTextBatchSafe(llamaContext, nextTokenArray, currentSequencePosition);
                currentSequencePosition++;

                await Task.Yield();
            }
        }

        /// <summary>
        /// Manages unmanaged pointers for vision inference to ensure safe disposal.
        /// </summary>
        private sealed class InferenceResources : IDisposable
        {
            public IntPtr SamplerChain { get; set; } = IntPtr.Zero;
            public IntPtr VisionBatchPtr { get; set; } = IntPtr.Zero;
            public IntPtr ChunksPtr { get; set; } = IntPtr.Zero;
            public IntPtr BitmapPtr { get; set; } = IntPtr.Zero;
            public IntPtr RawRgbPtr { get; set; } = IntPtr.Zero;

            public void Dispose()
            {
                if (SamplerChain != IntPtr.Zero) NativeLlamaMethods.LlamaSamplerFree(SamplerChain);
                if (VisionBatchPtr != IntPtr.Zero) NativeMtmdMethods.BatchFree(VisionBatchPtr);
                if (ChunksPtr != IntPtr.Zero) NativeMtmdMethods.InputChunksFree(ChunksPtr);
                if (BitmapPtr != IntPtr.Zero) NativeMtmdMethods.BitmapFree(BitmapPtr);
                if (RawRgbPtr != IntPtr.Zero) Marshal.FreeHGlobal(RawRgbPtr);
            }
        }
    }
}