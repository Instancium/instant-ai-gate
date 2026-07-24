// src/InstantAIGate.Infrastructure/Native/NativeLlamaApi.cs
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Robust facade for llama.cpp P/Invoke operations.
    /// Provides memory safety assertions, batch bounds checking, and strict error handling.
    /// Acts as an isolation layer for ChatAdapter, EmbeddingAdapter, and ModelProvider.
    /// </summary>
    public sealed class NativeLlamaApi
    {
        private NativeLlamaMethods.GgmlLogCallback? _nativeCallback;
        private readonly ILogger<NativeLlamaApi> _logger;

        public NativeLlamaApi(ILogger<NativeLlamaApi> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void LoadAllBackends()
        {
            try { NativeLlamaMethods.GgmlBackendLoadAll(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to load GGML backends."); }
            try { NativeLlamaMethods.LlamaBackendLoadAll(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to load LLaMA backends."); }
        }

        public void BackendInit() => NativeLlamaMethods.LlamaBackendInit();
        public void BackendFree() => NativeLlamaMethods.LlamaBackendFree();
        public bool SupportsGpuOffload() => NativeLlamaMethods.LlamaSupportsGpuOffload();

        public void SetLogCallback(NativeLogCallback callback)
        {
            _nativeCallback = (NativeLlamaMethods.GgmlLogLevel level, IntPtr text, IntPtr userData) =>
            {
                if (text == IntPtr.Zero) return;
                string? message = Marshal.PtrToStringAnsi(text)?.TrimEnd('\n', '\r');
                if (string.IsNullOrEmpty(message)) return;

                callback((NativeGgmlLogLevel)level, message);
            };

            try { NativeLlamaMethods.GgmlLogSet(_nativeCallback, IntPtr.Zero); } catch { }
            try { NativeLlamaMethods.LlamaLogSet(_nativeCallback, IntPtr.Zero); } catch { }
        }

        public IntPtr LoadModel(string path, int gpuLayers, int mainGpu, bool useMlock, bool useMmap, NativeLlamaSplitMode splitMode)
        {
            var p = NativeLlamaMethods.LlamaModelDefaultParams();
            p.NGpuLayers = gpuLayers;
            p.MainGpu = mainGpu;
            p.UseMlock = useMlock;
            p.UseMmap = useMmap;
            p.SplitMode = (NativeLlamaMethods.LlamaSplitMode)splitMode;

            IntPtr modelHandle = NativeLlamaMethods.LlamaModelLoadFromFile(path, p);
            if (modelHandle == IntPtr.Zero)
            {
                _logger.LogError("NativeLlamaMethods.LlamaModelLoadFromFile returned a zero pointer for path: {Path}", path);
                throw new InvalidOperationException("Failed to load native LLM weights into memory.");
            }

            return modelHandle;
        }

        public void FreeModel(IntPtr model)
        {
            if (model != IntPtr.Zero) NativeLlamaMethods.LlamaModelFree(model);
        }

        public IntPtr CreateContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, bool embeddings, NativeLlamaFlashAttnType flashAttn, NativeGgmlType kvType, bool offloadKqv)
        {
            if (model == IntPtr.Zero) throw new ArgumentException("Model handle cannot be zero.", nameof(model));

            var p = NativeLlamaMethods.LlamaContextDefaultParams();
            p.NCtx = nCtx;
            p.NBatch = nBatch;
            p.NUBatch = nBatch;
            p.NThreads = nThreads;
            p.NThreadsBatch = nThreads;
            p.Embeddings = embeddings;
            p.FlashAttnType = (NativeLlamaMethods.LlamaFlashAttnType)flashAttn;
            p.TypeK = (NativeLlamaMethods.GgmlType)kvType;
            p.TypeV = (NativeLlamaMethods.GgmlType)kvType;
            p.OffloadKqv = offloadKqv;

            IntPtr ctxHandle = NativeLlamaMethods.LlamaInitFromModel(model, p);
            if (ctxHandle == IntPtr.Zero)
            {
                _logger.LogError("NativeLlamaMethods.LlamaInitFromModel returned a zero pointer. Check VRAM capacity.");
                throw new InvalidOperationException("Failed to initialize inference context.");
            }

            return ctxHandle;
        }

        public IntPtr CreateEmbeddingContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, NativeLlamaFlashAttnType flashAttn)
        {
            if (model == IntPtr.Zero) throw new ArgumentException("Model handle cannot be zero.", nameof(model));

            var p = NativeLlamaMethods.LlamaContextDefaultParams();
            p.NCtx = nCtx;
            p.NBatch = nBatch;
            p.NUBatch = nBatch;
            p.NThreads = nThreads;
            p.NThreadsBatch = nThreads;
            p.Embeddings = true;
            p.PoolingType = NativeLlamaMethods.LlamaPoolingType.None;
            p.FlashAttnType = (NativeLlamaMethods.LlamaFlashAttnType)flashAttn;

            IntPtr ctxHandle = NativeLlamaMethods.LlamaInitFromModel(model, p);
            if (ctxHandle == IntPtr.Zero)
            {
                _logger.LogError("Failed to initialize embedding context.");
                throw new InvalidOperationException("Failed to initialize embedding context.");
            }

            return ctxHandle;
        }

        public void FreeContext(IntPtr context)
        {
            if (context != IntPtr.Zero) NativeLlamaMethods.LlamaFree(context);
        }

        public IntPtr GetMemory(IntPtr context) => NativeLlamaMethods.LlamaGetMemory(context);
        public void ClearMemory(IntPtr memory, bool clear) => NativeLlamaMethods.LlamaMemoryClear(memory, clear);

        public int Decode(IntPtr context, int batchSize, IntPtr tokenPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr)
        {
            if (context == IntPtr.Zero) throw new ArgumentException("Context cannot be zero.", nameof(context));
            if (batchSize <= 0) return 0;

            var batch = new NativeLlamaMethods.LlamaBatch
            {
                NTokens = batchSize,
                Token = tokenPtr,
                Embd = IntPtr.Zero,
                Pos = posPtr,
                NSeqId = nSeqIdPtr,
                SeqId = seqIdPtr,
                Logits = logitsPtr
            };

            int result = NativeLlamaMethods.LlamaDecode(context, batch);
            if (result != 0)
            {
                _logger.LogError("LlamaDecode failed during token evaluation with error code: {Code}. Possible KV-cache slot exhaustion.", result);
            }
            return result;
        }

        public int DecodeEmbeddings(IntPtr context, int batchSize, IntPtr embdPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr)
        {
            if (context == IntPtr.Zero) throw new ArgumentException("Context cannot be zero.", nameof(context));
            if (embdPtr == IntPtr.Zero) throw new ArgumentException("Embeddings pointer cannot be zero.", nameof(embdPtr));
            if (batchSize <= 0) return 0;

            var batch = new NativeLlamaMethods.LlamaBatch
            {
                NTokens = batchSize,
                Token = IntPtr.Zero,
                Embd = embdPtr,
                Pos = posPtr,
                NSeqId = nSeqIdPtr,
                SeqId = seqIdPtr,
                Logits = logitsPtr
            };

            int result = NativeLlamaMethods.LlamaDecode(context, batch);
            if (result != 0)
            {
                _logger.LogError("LlamaDecode failed during embeddings evaluation with error code: {Code}.", result);
            }
            return result;
        }

        public IntPtr ModelGetVocab(IntPtr model) => NativeLlamaMethods.LlamaModelGetVocab(model);
        public int ModelNEmbd(IntPtr model) => NativeLlamaMethods.LlamaModelNEmbd(model);
        public int VocabEos(IntPtr vocab) => NativeLlamaMethods.LlamaVocabEos(vocab);
        public IntPtr GetEmbeddingsIth(IntPtr context, int i) => NativeLlamaMethods.LlamaGetEmbeddingsIth(context, i);

        public int Tokenize(IntPtr vocab, string text, int textLen, int[] tokens, int maxTokens, bool addSpecial, bool parseSpecial)
        {
            if (vocab == IntPtr.Zero) throw new ArgumentException("Vocab pointer is zero.", nameof(vocab));
            return NativeLlamaMethods.LlamaTokenize(vocab, text, textLen, tokens, maxTokens, addSpecial, parseSpecial);
        }

        public int TokenToPiece(IntPtr vocab, int token, byte[] buffer, int bufferSize, int lstrip, bool special)
        {
            return NativeLlamaMethods.LlamaTokenToPiece(vocab, token, buffer, bufferSize, lstrip, special);
        }

        public NativeSamplerChainParams SamplerChainDefaultParams()
        {
            var nativeParams = NativeLlamaMethods.LlamaSamplerChainDefaultParams();
            return new NativeSamplerChainParams { NoPerf = nativeParams.NoPerf };
        }

        public IntPtr SamplerChainInit(NativeSamplerChainParams @params)
        {
            var nativeParams = new NativeLlamaMethods.LlamaSamplerChainParams { NoPerf = @params.NoPerf };
            return NativeLlamaMethods.LlamaSamplerChainInit(nativeParams);
        }

        public void SamplerChainAdd(IntPtr chain, IntPtr sampler)
        {
            if (chain != IntPtr.Zero && sampler != IntPtr.Zero)
            {
                NativeLlamaMethods.LlamaSamplerChainAdd(chain, sampler);
            }
        }

        public IntPtr SamplerInitTopK(int k) => NativeLlamaMethods.LlamaSamplerInitTopK(k);
        public IntPtr SamplerInitTopP(float p, nuint minKeep) => NativeLlamaMethods.LlamaSamplerInitTopP(p, minKeep);
        public IntPtr SamplerInitTemp(float temp) => NativeLlamaMethods.LlamaSamplerInitTemp(temp);
        public IntPtr SamplerInitDist(uint seed) => NativeLlamaMethods.LlamaSamplerInitDist(seed);

        public nint SamplerInitRepetition(float penaltyRepeat, float penaltyFreq, float penaltyPresent)
        {
            int penaltyLastN = -1;
            return NativeLlamaMethods.LlamaSamplerInitPenalties(penaltyLastN, penaltyRepeat, penaltyFreq, penaltyPresent);
        }

        public int SamplerSample(IntPtr sampler, IntPtr context, int index) => NativeLlamaMethods.LlamaSamplerSample(sampler, context, index);
        public void SamplerAccept(IntPtr sampler, int token) => NativeLlamaMethods.LlamaSamplerAccept(sampler, token);
        public void SamplerFree(IntPtr sampler)
        {
            if (sampler != IntPtr.Zero) NativeLlamaMethods.LlamaSamplerFree(sampler);
        }
    }
}