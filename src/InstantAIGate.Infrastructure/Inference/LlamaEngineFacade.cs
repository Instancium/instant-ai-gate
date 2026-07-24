using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace InstantAIGate.Infrastructure.Inference.Facades
{
    public sealed class LlamaEngineFacade
    {
        private readonly ILogger<LlamaEngineFacade> _logger;
        private readonly int _modelEmbdSize;

        public LlamaEngineFacade(IntPtr modelHandle, ILogger<LlamaEngineFacade> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modelEmbdSize = NativeLlamaMethods.LlamaModelNEmbd(modelHandle);
            if (_modelEmbdSize <= 0)
            {
                throw new InvalidOperationException("Failed to retrieve model embedding size.");
            }
        }

        public IntPtr CreateConfiguredSamplerChain(float temperature)
        {
            var samplerParams = NativeLlamaMethods.LlamaSamplerChainDefaultParams();
            IntPtr chain = NativeLlamaMethods.LlamaSamplerChainInit(samplerParams);

            IntPtr repetitionSampler = NativeLlamaMethods.LlamaSamplerInitPenalties(64, 1.1f, 0.1f, 0.1f);
            if (repetitionSampler != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaSamplerChainAdd(chain, repetitionSampler);
            }

            NativeLlamaMethods.LlamaSamplerChainAdd(chain, NativeLlamaMethods.LlamaSamplerInitTopK(40));
            NativeLlamaMethods.LlamaSamplerChainAdd(chain, NativeLlamaMethods.LlamaSamplerInitTopP(0.95f, 1));
            NativeLlamaMethods.LlamaSamplerChainAdd(chain, NativeLlamaMethods.LlamaSamplerInitTemp(temperature));

            uint seed = (uint)Random.Shared.Next();
            NativeLlamaMethods.LlamaSamplerChainAdd(chain, NativeLlamaMethods.LlamaSamplerInitDist(seed));

            return chain;
        }

        public void FreeSamplerChain(IntPtr chain)
        {
            if (chain != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaSamplerFree(chain);
            }
        }

        public int GetEosToken(IntPtr vocab) => NativeLlamaMethods.LlamaVocabEos(vocab);

        public int SampleNextTokenSafe(IntPtr samplerChain, IntPtr ctx, int currentPosition) =>
            NativeLlamaMethods.LlamaSamplerSample(samplerChain, ctx, currentPosition - 1);

        public void AcceptTokenSafe(IntPtr samplerChain, int token) =>
            NativeLlamaMethods.LlamaSamplerAccept(samplerChain, token);

        public string TokenToPieceSafe(IntPtr vocab, int token)
        {
            byte[] buffer = new byte[256];
            int length = NativeLlamaMethods.LlamaTokenToPiece(vocab, token, buffer, buffer.Length, 0, false);
            return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : string.Empty;
        }

        public void DecodePromptSegmentsSafe(IntPtr ctx, List<PromptSegment> segments, ref int currentSequencePosition)
        {
            foreach (var segment in segments)
            {
                if (segment.TextTokens != null)
                {
                    currentSequencePosition += DecodeTextBatchSafe(ctx, segment.TextTokens, currentSequencePosition);
                }
                else if (segment.VisionData != null)
                {
                    currentSequencePosition += DecodeVisionBatchSafe(ctx, segment.VisionData, currentSequencePosition);
                }
            }
        }

        public unsafe int DecodeTextBatchSafe(IntPtr ctx, int[] tokens, int startPosition)
        {
            if (ctx == IntPtr.Zero) throw new ArgumentException("Context handle cannot be zero.", nameof(ctx));
            if (tokens == null || tokens.Length == 0) return 0;

            NativeLlamaMethods.LlamaBatch batch = NativeLlamaMethods.LlamaBatchInit(tokens.Length, 0, 1);
            try
            {
                int* tokenPtr = (int*)batch.Token;
                int* posPtr = (int*)batch.Pos;
                int* nSeqIdPtr = (int*)batch.NSeqId;
                int** seqIdPtr = (int**)batch.SeqId;
                byte* logitsPtr = (byte*)batch.Logits;

                for (int i = 0; i < tokens.Length; i++)
                {
                    tokenPtr[i] = tokens[i];
                    posPtr[i] = startPosition + i;
                    nSeqIdPtr[i] = 1;
                    seqIdPtr[i][0] = 0;
                    logitsPtr[i] = 0;
                }
                logitsPtr[tokens.Length - 1] = 1;
                batch.NTokens = tokens.Length;

                int result = NativeLlamaMethods.LlamaDecode(ctx, batch);
                if (result != 0) throw new InvalidOperationException($"Text batch decode failed with code: {result}.");
                return tokens.Length;
            }
            finally { NativeLlamaMethods.LlamaBatchFree(batch); }
        }

        public unsafe int DecodeVisionBatchSafe(IntPtr ctx, ExtractedVisionData visionData, int startPosition)
        {
            if (ctx == IntPtr.Zero) throw new ArgumentException("Context handle cannot be zero.", nameof(ctx));
            if (visionData.EmbeddingsPtr == IntPtr.Zero) throw new ArgumentException("Embeddings pointer cannot be zero.", nameof(visionData));
            if (visionData.TokenCount <= 0) return 0;

            NativeLlamaMethods.LlamaBatch batch = NativeLlamaMethods.LlamaBatchInit(visionData.TokenCount, _modelEmbdSize, 1);
            try
            {
                long embdBytes = (long)visionData.TokenCount * _modelEmbdSize * sizeof(float);
                Buffer.MemoryCopy((void*)visionData.EmbeddingsPtr, (void*)batch.Embd, embdBytes, embdBytes);

                int* posPtr = (int*)batch.Pos;
                for (int i = 0; i < visionData.TokenCount; i++) posPtr[i] = (int)visionData.Positions[i].T;

                int* nSeqIdPtr = (int*)batch.NSeqId;
                int** seqIdPtr = (int**)batch.SeqId;
                for (int i = 0; i < visionData.TokenCount; i++)
                {
                    nSeqIdPtr[i] = 1;
                    seqIdPtr[i][0] = 0;
                }

                byte* logitsPtr = (byte*)batch.Logits;
                for (int i = 0; i < visionData.TokenCount; i++) logitsPtr[i] = 0;

                batch.NTokens = visionData.TokenCount;
                int result = NativeLlamaMethods.LlamaDecode(ctx, batch);
                if (result != 0) throw new InvalidOperationException($"Vision batch decode failed with code: {result}.");
                return visionData.TokenCount;
            }
            finally { NativeLlamaMethods.LlamaBatchFree(batch); }
        }
    }
}