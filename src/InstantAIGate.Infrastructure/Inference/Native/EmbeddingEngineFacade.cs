using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Application.Interfaces.Inference;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    public class EmbeddingEngineFacade : IEmbeddingEngineFacade
    {
        private readonly ModelManager _modelManager;
        private readonly ILogger<EmbeddingEngineFacade> _logger;

        public EmbeddingEngineFacade(ModelManager modelManager, ILogger<EmbeddingEngineFacade> logger)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(List<string> inputs, InferenceSettings settings, CancellationToken ct = default)
        {
            using var model = await _modelManager.AcquireModelAsync(settings.ModelId, ct);
            IntPtr modelHandle = model.Handle;

            int embeddingLength = NativeLlamaMethods.LlamaModelNEmbd(modelHandle);
            if (embeddingLength <= 0)
            {
                throw new InvalidOperationException("The requested model does not support or export dense embedding layers.");
            }

            int nCtx = settings.MaxTokens;
            int maxBatchSize = settings.BatchSize;

            var ctxParams = NativeLlamaMethods.LlamaContextDefaultParams();
            ctxParams.NCtx = (uint)nCtx;
            ctxParams.NBatch = (uint)maxBatchSize;
            ctxParams.NUBatch = (uint)maxBatchSize;
            ctxParams.Embeddings = true;
            ctxParams.PoolingType = NativeLlamaMethods.LlamaPoolingType.None;

            IntPtr ctxHandle = NativeLlamaMethods.LlamaInitFromModel(modelHandle, ctxParams);
            if (ctxHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize dedicated embedding context.");
            }

            try
            {
                IntPtr vocab = NativeLlamaMethods.LlamaModelGetVocab(modelHandle);
                var results = new List<float[]>(inputs.Count);

                foreach (var text in inputs)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    byte[] textBytes = Encoding.UTF8.GetBytes(text);
                    int[] tokens = new int[textBytes.Length + 4];

                    int nTokens = NativeLlamaMethods.LlamaTokenize(vocab, text, textBytes.Length, tokens, tokens.Length, true, true);

                    if (nTokens < 0)
                    {
                        Array.Resize(ref tokens, Math.Abs(nTokens));
                        nTokens = NativeLlamaMethods.LlamaTokenize(vocab, text, textBytes.Length, tokens, tokens.Length, true, true);
                    }

                    if (nTokens <= 0)
                    {
                        continue;
                    }

                    if (nTokens > nCtx)
                    {
                        _logger.LogWarning("Input text exceeds context size ({Tokens}/{Limit}). Truncating to context limit.", nTokens, nCtx);
                        nTokens = nCtx;
                    }

                    NativeLlamaMethods.LlamaMemoryClear(NativeLlamaMethods.LlamaGetMemory(ctxHandle), true);

                    float[] aggregatedVector = new float[embeddingLength];
                    int validVectorsCount = 0;
                    bool decodeFailed = false;

                    unsafe
                    {
                        NativeLlamaMethods.LlamaBatch batch = NativeLlamaMethods.LlamaBatchInit(maxBatchSize, 0, 1);
                        try
                        {
                            int* tokenPtr = (int*)batch.Token;
                            int* posPtr = (int*)batch.Pos;
                            int* nSeqIdPtr = (int*)batch.NSeqId;
                            int** seqIdPtr = (int**)batch.SeqId;
                            byte* logitsPtr = (byte*)batch.Logits;

                            for (int i = 0; i < nTokens; i += maxBatchSize)
                            {
                                ct.ThrowIfCancellationRequested();

                                int evalSize = Math.Min(nTokens - i, maxBatchSize);
                                batch.NTokens = evalSize;

                                for (int j = 0; j < evalSize; j++)
                                {
                                    tokenPtr[j] = tokens[i + j];
                                    posPtr[j] = i + j;
                                    nSeqIdPtr[j] = 1;
                                    seqIdPtr[j][0] = 0;
                                    logitsPtr[j] = 1;
                                }

                                int decodeStatus = NativeLlamaMethods.LlamaDecode(ctxHandle, batch);
                                if (decodeStatus != 0)
                                {
                                    _logger.LogError("Embedding execution tracking failed on chunk. Error code: {Code}", decodeStatus);
                                    decodeFailed = true;
                                    break;
                                }

                                for (int j = 0; j < evalSize; j++)
                                {
                                    IntPtr ptrEmbeddings = NativeLlamaMethods.LlamaGetEmbeddingsIth(ctxHandle, j);
                                    if (ptrEmbeddings != IntPtr.Zero)
                                    {
                                        float[] tokenVector = new float[embeddingLength];
                                        Marshal.Copy(ptrEmbeddings, tokenVector, 0, embeddingLength);

                                        for (int v = 0; v < embeddingLength; v++)
                                        {
                                            aggregatedVector[v] += tokenVector[v];
                                        }

                                        validVectorsCount++;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            NativeLlamaMethods.LlamaBatchFree(batch);
                        }
                    }

                    if (decodeFailed)
                    {
                        continue;
                    }

                    if (validVectorsCount > 0)
                    {
                        for (int v = 0; v < embeddingLength; v++)
                        {
                            aggregatedVector[v] /= validVectorsCount;
                        }

                        float magnitude = 0f;
                        for (int v = 0; v < embeddingLength; v++)
                        {
                            magnitude += aggregatedVector[v] * aggregatedVector[v];
                        }

                        magnitude = MathF.Sqrt(magnitude);

                        if (magnitude > 1e-12f)
                        {
                            for (int v = 0; v < embeddingLength; v++)
                            {
                                aggregatedVector[v] /= magnitude;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No embeddings were extracted for input text. Skipping normalization.");
                    }

                    results.Add(aggregatedVector);
                }

                return results;
            }
            finally
            {
                NativeLlamaMethods.LlamaFree(ctxHandle);
            }
        }
    }
}