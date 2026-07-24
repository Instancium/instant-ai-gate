using InstantAIGate.Infrastructure.Inference.Facades;
using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
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

        public IAsyncEnumerable<string> StreamVisionResponseAsync(
            VisionContext visionContext,
            IntPtr llamaContext,
            IntPtr llamaVocab,
            string prompt,
            byte[] imageBytes,
            uint width,
            uint height,
            string hashId,
            int maxTokens,
            float temperature = 0.1f,
            CancellationToken ct = default)
        {
            return StreamVisionResponseAsyncCore(visionContext, llamaContext, llamaVocab, prompt, imageBytes, width, height, hashId, maxTokens, temperature, ct);
        }

        private async IAsyncEnumerable<string> StreamVisionResponseAsyncCore(
            VisionContext visionContext,
            IntPtr llamaContext,
            IntPtr llamaVocab,
            string prompt,
            byte[] imageBytes,
            uint width,
            uint height,
            string hashId,
            int maxTokens,
            float temperature,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var (bitmapPtr, rawRgbPtr) = _visionFacade.PrepareMediaValidated(imageBytes, width, height, hashId);
            var (chunksPtr, visionChunkPtr) = _visionFacade.TokenizeAndValidateChunks(visionContext.Handle, prompt, bitmapPtr);
            IntPtr visionBatchPtr = _visionFacade.EncodeBatchSafe(visionContext.Handle, chunksPtr);
            IntPtr samplerChain = _llamaFacade.CreateConfiguredSamplerChain(temperature);

            try
            {
                List<PromptSegment> segments = _visionFacade.ParseChunksIntoSegments(visionContext.Handle, chunksPtr, visionBatchPtr);
                int currentSequencePosition = 0;
                _llamaFacade.DecodePromptSegmentsSafe(llamaContext, segments, ref currentSequencePosition);

                int generatedTokens = 0;
                int eosToken = _llamaFacade.GetEosToken(llamaVocab);
                int lastTokenId = -1;
                int repetitionCount = 0;

                while (generatedTokens < maxTokens)
                {
                    ct.ThrowIfCancellationRequested();

                    int nextToken = _llamaFacade.SampleNextTokenSafe(samplerChain, llamaContext, currentSequencePosition);
                    if (nextToken == eosToken || nextToken < 0) break;

                    if (nextToken == lastTokenId)
                    {
                        repetitionCount++;
                        if (repetitionCount > 3)
                        {
                            _logger.LogWarning("Generation stopped due to infinite repetition loop.");
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

                    string piece = _llamaFacade.TokenToPieceSafe(llamaVocab, nextToken);
                    yield return piece;

                    int[] nextTokenArray = { nextToken };
                    _llamaFacade.DecodeTextBatchSafe(llamaContext, nextTokenArray, currentSequencePosition);
                    currentSequencePosition++;

                    await Task.Yield();
                }
            }
            finally
            {
                _llamaFacade.FreeSamplerChain(samplerChain);
                _visionFacade.FreeVisionResources(visionBatchPtr, chunksPtr, bitmapPtr, rawRgbPtr);
            }
        }
    }
}