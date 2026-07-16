using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native;

/// <summary>
/// Default implementation of <see cref="INativeLlamaApi"/> that delegates all calls to NativeMethods.
/// This is the only class that directly invokes P/Invoke methods.
/// </summary>
public sealed class NativeLlamaApi : INativeLlamaApi
{
    private NativeMethods.ggml_log_callback? _nativeCallback;

    public void LoadAllBackends()
    {
        try { NativeMethods.ggml_backend_load_all_ggml(); } catch { }
        try { NativeMethods.ggml_backend_load_all_llama(); } catch { }
    }

    public int GetModelEmbeddingSize(IntPtr model)
    {
        return NativeMethods.llama_model_n_embd(model);
    }

    public int DecodeEmbeddings(IntPtr context, int batchSize, IntPtr embdPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr)
    {
        var batch = new NativeMethods.LlamaBatch
        {
            n_tokens = batchSize,
            token = IntPtr.Zero, // Token array is ignored when using embeddings
            embd = embdPtr,      // Pass the float array pointer here
            pos = posPtr,
            n_seq_id = nSeqIdPtr,
            seq_id = seqIdPtr,
            logits = logitsPtr
        };

        return NativeMethods.llama_decode(context, batch);
    }

    public void BackendInit() => NativeMethods.llama_backend_init();
    public void BackendFree() => NativeMethods.llama_backend_free();
    public bool SupportsGpuOffload() => NativeMethods.llama_supports_gpu_offload();

    public void SetLogCallback(NativeLogCallback callback)
    {
        _nativeCallback = (NativeMethods.ggml_log_level level, IntPtr text, IntPtr user_data) =>
        {
            if (text == IntPtr.Zero) return;
            string? message = Marshal.PtrToStringAnsi(text)?.TrimEnd('\n', '\r');
            if (string.IsNullOrEmpty(message)) return;

            var cleanLevel = (NativeGgmlLogLevel)level;
            callback(cleanLevel, message!); // Добавлен оператор null-forgiving
        };

        try { NativeMethods.ggml_log_set(_nativeCallback, IntPtr.Zero); } catch { }
        try { NativeMethods.llama_log_set(_nativeCallback, IntPtr.Zero); } catch { }
    }

    public IntPtr GetMemory(IntPtr context) => NativeMethods.llama_get_memory(context);
    public void ClearMemory(IntPtr memory, bool clear) => NativeMethods.llama_memory_clear(memory, clear);
    public void FreeModel(IntPtr model) => NativeMethods.llama_model_free(model);
    public void FreeContext(IntPtr context) => NativeMethods.llama_free(context);

    public IntPtr LoadModel(string path, int gpuLayers, int mainGpu, bool useMlock, bool useMmap, NativeLlamaSplitMode splitMode)
    {
        var p = NativeMethods.llama_model_default_params();
        p.n_gpu_layers = gpuLayers;
        p.main_gpu = mainGpu;
        p.use_mlock = useMlock;
        p.use_mmap = useMmap;
        p.split_mode = (NativeMethods.llama_split_mode)splitMode;

        return NativeMethods.llama_model_load_from_file(path, p);
    }

    public IntPtr CreateContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, bool embeddings, NativeLlamaFlashAttnType flashAttn, NativeGgmlType kvType, bool offloadKqv)
    {
        var p = NativeMethods.llama_context_default_params();
        p.n_ctx = nCtx;
        p.n_batch = nBatch;
        p.n_ubatch = nBatch;
        p.n_threads = nThreads;
        p.n_threads_batch = nThreads;
        p.embeddings = embeddings;
        p.flash_attn_type = (NativeMethods.llama_flash_attn_type)flashAttn;
        p.type_k = (NativeMethods.ggml_type)kvType;
        p.type_v = (NativeMethods.ggml_type)kvType;
        p.offload_kqv = offloadKqv;

        return NativeMethods.llama_init_from_model(model, p);
    }

    // === Tokenization ===
    public IntPtr ModelGetVocab(IntPtr model) => NativeMethods.llama_model_get_vocab(model);

    public int Tokenize(IntPtr vocab, string text, int textLen, int[] tokens, int maxTokens, bool addSpecial, bool parseSpecial)
        => NativeMethods.llama_tokenize(vocab, text, textLen, tokens, maxTokens, addSpecial, parseSpecial);

    public int VocabEos(IntPtr vocab) => NativeMethods.llama_vocab_eos(vocab);

    public int TokenToPiece(IntPtr vocab, int token, byte[] buffer, int bufferSize, int lstrip, bool special)
        => NativeMethods.llama_token_to_piece(vocab, token, buffer, bufferSize, lstrip, special);

    // === Sampler ===
    public NativeSamplerChainParams SamplerChainDefaultParams()
    {
        var nativeParams = NativeMethods.llama_sampler_chain_default_params();
        return new NativeSamplerChainParams { NoPerf = nativeParams.no_perf };
    }

    public IntPtr SamplerChainInit(NativeSamplerChainParams @params)
    {
        var nativeParams = new NativeMethods.llama_sampler_chain_params { no_perf = @params.NoPerf };
        return NativeMethods.llama_sampler_chain_init(nativeParams);
    }

    public void SamplerChainAdd(IntPtr chain, IntPtr sampler)
        => NativeMethods.llama_sampler_chain_add(chain, sampler);

    public IntPtr SamplerInitTopK(int k) => NativeMethods.llama_sampler_init_top_k(k);
    public IntPtr SamplerInitTopP(float p, nuint minKeep) => NativeMethods.llama_sampler_init_top_p(p, minKeep);
    public IntPtr SamplerInitTemp(float temp) => NativeMethods.llama_sampler_init_temp(temp);
    public IntPtr SamplerInitDist(uint seed) => NativeMethods.llama_sampler_init_dist(seed);
    public int SamplerSample(IntPtr sampler, IntPtr context, int index) => NativeMethods.llama_sampler_sample(sampler, context, index);
    public void SamplerFree(IntPtr sampler) => NativeMethods.llama_sampler_free(sampler);

    // === Inference ===
    public int Decode(IntPtr context, int batchSize, IntPtr tokenPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr)
    {
        var batch = new NativeMethods.LlamaBatch
        {
            n_tokens = batchSize,
            token = tokenPtr,
            embd = IntPtr.Zero,
            pos = posPtr,
            n_seq_id = nSeqIdPtr,
            seq_id = seqIdPtr,
            logits = logitsPtr
        };

        return NativeMethods.llama_decode(context, batch);
    }

    public nint SamplerInitRepetition(float penaltyRepeat, float penaltyFreq, float penaltyPresent)
    {
        // penalty_last_n: number of last tokens to penalize
        // 0 = disable penalty
        // -1 = use context size (penalize all tokens in context)
        // Positive value = penalize last N tokens
        int penaltyLastN = -1; // Use context size for comprehensive penalty

        return NativeMethods.llama_sampler_init_penalties(
            penaltyLastN,
            penaltyRepeat,
            penaltyFreq,
            penaltyPresent
        );
    }

    public int ModelNEmbd(IntPtr model)
    {
        return NativeMethods.llama_model_n_embd(model);
    }

    public IntPtr GetEmbeddingsIth(IntPtr context, int i)
    {
        return NativeMethods.llama_get_embeddings_ith(context, i);
    }

    public IntPtr CreateEmbeddingContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, NativeLlamaFlashAttnType flashAttn)
    {
        var p = NativeMethods.llama_context_default_params();
        p.n_ctx = nCtx;
        p.n_batch = nBatch;
        p.n_ubatch = nBatch;
        p.n_threads = nThreads;
        p.n_threads_batch = nThreads;
        p.embeddings = true;
        p.pooling_type = NativeMethods.llama_pooling_type.LLAMA_POOLING_TYPE_NONE;
        p.flash_attn_type = (NativeMethods.llama_flash_attn_type)flashAttn;

        return NativeMethods.llama_init_from_model(model, p);
    }

}