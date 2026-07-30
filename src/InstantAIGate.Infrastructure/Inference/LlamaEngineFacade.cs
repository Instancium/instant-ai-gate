using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Infrastructure.Inference.Native;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace InstantAIGate.Infrastructure.Inference
{
    public class InferenceSettings
    {
        public string ModelId { get; set; } = string.Empty;
        public float Temperature { get; set; } = 0.7f;
        public int TopK { get; set; } = 40;
        public float TopP { get; set; } = 0.9f;
        public int MaxTokens { get; set; } = 512;
        public int BatchSize { get; set; } = 512;
        public uint? Seed { get; set; }
    }

    public interface ILlamaEngineFacade
    {
        Task<int> GetTokenCountAsync(string modelId, string text, CancellationToken ct = default);
        IAsyncEnumerable<string> StreamGenerationAsync(string prompt, InferenceSettings settings, CancellationToken ct = default);
    }



    public partial class LlamaEngineFacade(ModelManager _modelManager) : ILlamaEngineFacade
    {
        public async Task<int> GetTokenCountAsync(string modelId, string text, CancellationToken ct = default)
        {
            using var model = await _modelManager.AcquireModelAsync(modelId, ct);
            IntPtr vocab = NativeLlamaMethods.LlamaModelGetVocab(model.Handle);

            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            int[] tokens = new int[textBytes.Length + 64];

            int count = NativeLlamaMethods.LlamaTokenize(vocab, text, textBytes.Length, tokens, tokens.Length, false, true);

            return count < 0 ? -count : count;
        }

        public async IAsyncEnumerable<string> StreamGenerationAsync(
            string prompt,
            InferenceSettings settings,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var model = await _modelManager.AcquireModelAsync(settings.ModelId, ct);
            using var context = await _modelManager.AcquireContextAsync(settings.ModelId, ct);

            IntPtr vocab = NativeLlamaMethods.LlamaModelGetVocab(model.Handle);
            IntPtr ctxHandle = context.TextContext.Handle;

            byte[] promptBytes = Encoding.UTF8.GetBytes(prompt);
            int[] tokens = new int[promptBytes.Length + 64];
            int nTokens = NativeLlamaMethods.LlamaTokenize(vocab, prompt, promptBytes.Length, tokens, tokens.Length, false, true);

            if (nTokens < 0)
            {
                tokens = new int[-nTokens];
                nTokens = NativeLlamaMethods.LlamaTokenize(vocab, prompt, promptBytes.Length, tokens, tokens.Length, false, true);
            }

            var chainParams = NativeLlamaMethods.LlamaSamplerChainDefaultParams();
            IntPtr sampler = NativeLlamaMethods.LlamaSamplerChainInit(chainParams);

            try
            {
                IntPtr penaltySampler = NativeLlamaMethods.LlamaSamplerInitPenalties(1024, 1.15f, 0.05f, 0.05f);
                if (penaltySampler != IntPtr.Zero)
                {
                    NativeLlamaMethods.LlamaSamplerChainAdd(sampler, penaltySampler);
                }

                NativeLlamaMethods.LlamaSamplerChainAdd(sampler, NativeLlamaMethods.LlamaSamplerInitTopK(settings.TopK > 0 ? settings.TopK : 40));
                NativeLlamaMethods.LlamaSamplerChainAdd(sampler, NativeLlamaMethods.LlamaSamplerInitTopP(settings.TopP > 0 ? settings.TopP : 0.9f, 1));
                NativeLlamaMethods.LlamaSamplerChainAdd(sampler, NativeLlamaMethods.LlamaSamplerInitTemp(settings.Temperature > 0 ? settings.Temperature : 0.7f));

                uint activeSeed = settings.Seed.HasValue ? settings.Seed.Value : (uint)Random.Shared.Next();
                NativeLlamaMethods.LlamaSamplerChainAdd(sampler, NativeLlamaMethods.LlamaSamplerInitDist(activeSeed));

                for (int i = 0; i < nTokens; i++)
                {
                    NativeLlamaMethods.LlamaSamplerAccept(sampler, tokens[i]);
                }

                int lastEvalBatchSize = 0;

                unsafe
                {
                    int maxBatchSize = settings.BatchSize > 0 ? settings.BatchSize : 512;
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

                            int evalBatchSize = Math.Min(nTokens - i, maxBatchSize);
                            lastEvalBatchSize = evalBatchSize;

                            for (int j = 0; j < evalBatchSize; j++)
                            {
                                tokenPtr[j] = tokens[i + j];
                                posPtr[j] = i + j;
                                nSeqIdPtr[j] = 1;
                                seqIdPtr[j][0] = 0;
                                logitsPtr[j] = (byte)((i + j == nTokens - 1) ? 1 : 0);
                            }

                            batch.NTokens = evalBatchSize;

                            int evalResult = NativeLlamaMethods.LlamaDecode(ctxHandle, batch);
                            if (evalResult != 0)
                            {
                                throw new InvalidOperationException($"Prompt evaluation failed with code: {evalResult}");
                            }
                        }
                    }
                    finally
                    {
                        NativeLlamaMethods.LlamaBatchFree(batch);
                    }
                }

                int eos = NativeLlamaMethods.LlamaVocabEos(vocab);
                int generated = 0;
                int currentPos = nTokens;

                var utf8Decoder = Encoding.UTF8.GetDecoder();
                byte[] pieceBuffer = new byte[256];
                char[] charBuffer = new char[512];

                while (generated < settings.MaxTokens)
                {
                    ct.ThrowIfCancellationRequested();

                    int logitIndex = (generated == 0) ? (lastEvalBatchSize - 1) : 0;

                    int token = NativeLlamaMethods.LlamaSamplerSample(sampler, ctxHandle, logitIndex);

                    if (token == eos || token < 0)
                    {
                        break;
                    }

                    NativeLlamaMethods.LlamaSamplerAccept(sampler, token);

                    int pieceSize = NativeLlamaMethods.LlamaTokenToPiece(vocab, token, pieceBuffer, pieceBuffer.Length, 0, true);
                    if (pieceSize > pieceBuffer.Length)
                    {
                        pieceBuffer = new byte[pieceSize];
                        pieceSize = NativeLlamaMethods.LlamaTokenToPiece(vocab, token, pieceBuffer, pieceBuffer.Length, 0, true);
                    }

                    if (pieceSize > 0)
                    {
                        int charsDecoded = utf8Decoder.GetChars(pieceBuffer, 0, pieceSize, charBuffer, 0, false);
                        if (charsDecoded > 0)
                        {
                            yield return new string(charBuffer, 0, charsDecoded);
                        }
                    }

                    generated++;

                    unsafe
                    {
                        NativeLlamaMethods.LlamaBatch singleBatch = NativeLlamaMethods.LlamaBatchInit(1, 0, 1);
                        try
                        {
                            ((int*)singleBatch.Token)[0] = token;
                            ((int*)singleBatch.Pos)[0] = currentPos++;
                            ((int*)singleBatch.NSeqId)[0] = 1;
                            ((int**)singleBatch.SeqId)[0][0] = 0;
                            ((byte*)singleBatch.Logits)[0] = 1;
                            singleBatch.NTokens = 1;

                            int stepResult = NativeLlamaMethods.LlamaDecode(ctxHandle, singleBatch);
                            if (stepResult != 0)
                            {
                                break;
                            }
                        }
                        finally
                        {
                            NativeLlamaMethods.LlamaBatchFree(singleBatch);
                        }
                    }
                }

                int finalChars = utf8Decoder.GetChars(pieceBuffer, 0, 0, charBuffer, 0, true);
                if (finalChars > 0)
                {
                    yield return new string(charBuffer, 0, finalChars);
                }
            }
            finally
            {
                NativeLlamaMethods.LlamaSamplerFree(sampler);
            }
        }
    }
}