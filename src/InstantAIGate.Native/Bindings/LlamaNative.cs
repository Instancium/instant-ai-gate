// SPDX-FileCopyrightText: (c) InstantAI Gate Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using InstantAIGate.Native.Bindings;

namespace InstantAIGate.Native.Bindings;

/// <summary>
/// P/Invoke bindings for llama.h native functions.
/// </summary>
internal static partial class LlamaNative
{
    private const string LibraryName = "llama";

    #region Backend Management

    /// <summary>
    /// Initializes the llama backend.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_backend_init();

    /// <summary>
    /// Frees resources allocated by the llama backend.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_backend_free();

    /// <summary>
    /// Initializes NUMA strategy for memory allocation.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_numa_init(int numa);

    /// <summary>
    /// Attaches a threadpool to a context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_attach_threadpool(IntPtr ctx, IntPtr threadpool, IntPtr threadpoolBatch);

    /// <summary>
    /// Detaches threadpool from a context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_detach_threadpool(IntPtr ctx);

    #endregion

    #region Model Management

    /// <summary>
    /// Gets default model parameters.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaModelParams llama_model_default_params();

    /// <summary>
    /// Loads a model from a file path.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr llama_model_load_from_file(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathFile,
        in LlamaModelParams params_,
        nuint pathLen);

    /// <summary>
    /// Loads a model using split files.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_model_load_from_splits(
        IntPtr[] paths,
        nuint nPaths,
        in LlamaModelParams params_);

    /// <summary>
    /// Frees a loaded model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_model_free(IntPtr model);

    /// <summary>
    /// Gets the vocabulary from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_model_get_vocab(IntPtr model);

    /// <summary>
    /// Gets the training context size from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_ctx_train(IntPtr model);

    /// <summary>
    /// Gets the embedding size from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_embd(IntPtr model);

    /// <summary>
    /// Gets the input embedding size from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_embd_inp(IntPtr model);

    /// <summary>
    /// Gets the output embedding size from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_embd_out(IntPtr model);

    /// <summary>
    /// Gets the number of layers from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_layer(IntPtr model);

    /// <summary>
    /// Gets the number of attention heads from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_head(IntPtr model);

    /// <summary>
    /// Gets the number of key/value heads from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_head_kv(IntPtr model);

    /// <summary>
    /// Gets the RoPE frequency scale from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float llama_model_rope_freq_scale_train(IntPtr model);

    /// <summary>
    /// Gets metadata value as a string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int llama_model_meta_val_str(
        IntPtr model,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        IntPtr buf,
        nuint bufSize);

    /// <summary>
    /// Gets the count of metadata keys.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_meta_count(IntPtr model);

    /// <summary>
    /// Gets metadata key by index.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_meta_key_by_index(
        IntPtr model,
        int i,
        IntPtr buf,
        nuint bufSize);

    /// <summary>
    /// Gets metadata value by index.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_meta_val_str_by_index(
        IntPtr model,
        int i,
        IntPtr buf,
        nuint bufSize);

    /// <summary>
    /// Gets model description.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_desc(
        IntPtr model,
        IntPtr buf,
        nuint bufSize);

    /// <summary>
    /// Gets model file type.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaFType llama_model_ftype(IntPtr model);

    /// <summary>
    /// Gets model size in bytes.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong llama_model_size(IntPtr model);

    /// <summary>
    /// Gets chat template by name.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr llama_model_chat_template(
        IntPtr model,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    /// <summary>
    /// Gets total number of model parameters.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong llama_model_n_params(IntPtr model);

    /// <summary>
    /// Checks if model has encoder.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_model_has_encoder(IntPtr model);

    /// <summary>
    /// Checks if model has decoder.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_model_has_decoder(IntPtr model);

    /// <summary>
    /// Gets decoder start token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_decoder_start_token(IntPtr model);

    /// <summary>
    /// Gets RoPE type of the model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaRopeType llama_model_rope_type(IntPtr model);

    /// <summary>
    /// Gets the model associated with a context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_model(IntPtr ctx);

    #endregion

    #region Context Management

    /// <summary>
    /// Gets default context parameters.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaContextParams llama_context_default_params();

    /// <summary>
    /// Initializes a context from a model.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_init_from_model(
        IntPtr model,
        in LlamaContextParams params_);

    /// <summary>
    /// Frees a context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_free(IntPtr ctx);

    /// <summary>
    /// Gets context size.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_n_ctx(IntPtr ctx);

    /// <summary>
    /// Gets maximum sequence count.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_n_ctx_seq(IntPtr ctx);

    /// <summary>
    /// Gets batch size.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_n_batch(IntPtr ctx);

    /// <summary>
    /// Gets physical batch size.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_n_ubatch(IntPtr ctx);

    /// <summary>
    /// Gets maximum sequence count.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_n_seq_max(IntPtr ctx);

    /// <summary>
    /// Gets pooling type.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaPoolingType llama_pooling_type(IntPtr ctx);

    /// <summary>
    /// Gets memory handle from context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_memory(IntPtr ctx);

    #endregion

    #region Encoding and Decoding

    /// <summary>
    /// Encodes a batch of tokens.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_encode(IntPtr ctx, in LlamaBatch batch);

    /// <summary>
    /// Decodes a batch of tokens.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_decode(IntPtr ctx, in LlamaBatch batch);

    /// <summary>
    /// Gets logits for all tokens.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_logits(IntPtr ctx);

    /// <summary>
    /// Gets logits for a specific token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_logits_ith(IntPtr ctx, int i);

    /// <summary>
    /// Gets embeddings for all sequences.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_embeddings(IntPtr ctx);

    /// <summary>
    /// Gets embeddings for a specific sequence.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_embeddings_seq(IntPtr ctx, int seqId);

    #endregion

    #region Vocabulary

    /// <summary>
    /// Gets vocabulary type.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaVocabType llama_vocab_type(IntPtr vocab);

    /// <summary>
    /// Gets vocabulary size.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_n_tokens(IntPtr vocab);

    /// <summary>
    /// Gets text for a token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_vocab_get_text(IntPtr vocab, int token);

    /// <summary>
    /// Gets score for a token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float llama_vocab_get_score(IntPtr vocab, int token);

    /// <summary>
    /// Gets attributes for a token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaTokenAttr llama_vocab_get_attr(IntPtr vocab, int token);

    /// <summary>
    /// Checks if token is end-of-generation.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_vocab_is_eog(IntPtr vocab, int token);

    /// <summary>
    /// Checks if token is control token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_vocab_is_control(IntPtr vocab, int token);

    /// <summary>
    /// Gets beginning-of-sentence token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_bos(IntPtr vocab);

    /// <summary>
    /// Gets end-of-sentence token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_eos(IntPtr vocab);

    /// <summary>
    /// Gets end-of-turn token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_eot(IntPtr vocab);

    /// <summary>
    /// Gets sentence separator token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_sep(IntPtr vocab);

    /// <summary>
    /// Gets newline token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_nl(IntPtr vocab);

    /// <summary>
    /// Gets padding token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_pad(IntPtr vocab);

    /// <summary>
    /// Gets add BOS setting.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_vocab_get_add_bos(IntPtr vocab);

    /// <summary>
    /// Gets add EOS setting.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_vocab_get_add_eos(IntPtr vocab);

    /// <summary>
    /// Tokenizes text into tokens.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int llama_tokenize(
        IntPtr vocab,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        int textLen,
        IntPtr tokens,
        int nTokensMax,
        [MarshalAs(UnmanagedType.I1)] bool addSpecial,
        [MarshalAs(UnmanagedType.I1)] bool parseSpecial);

    /// <summary>
    /// Converts token to text piece.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_to_piece(
        IntPtr vocab,
        int token,
        IntPtr buf,
        int bufSize,
        [MarshalAs(UnmanagedType.I1)] bool special);

    /// <summary>
    /// Detokenizes tokens to text.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_detokenize(
        IntPtr vocab,
        IntPtr tokens,
        int nTokens,
        IntPtr text,
        int textSize,
        [MarshalAs(UnmanagedType.I1)] bool removeSpecial);

    #endregion

    #region Sampling

    /// <summary>
    /// Gets default sampler chain parameters.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaSamplerChainParams llama_sampler_chain_default_params();

    /// <summary>
    /// Initializes a sampler chain.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_chain_init(in LlamaSamplerChainParams params_);

    /// <summary>
    /// Adds a sampler to the chain.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_sampler_chain_add(IntPtr chain, IntPtr sampler);

    /// <summary>
    /// Gets sampler at index.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_chain_get(IntPtr chain, int i);

    /// <summary>
    /// Gets number of samplers in chain.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_sampler_chain_n(IntPtr chain);

    /// <summary>
    /// Removes sampler at index.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_chain_remove(IntPtr chain, int i);

    /// <summary>
    /// Frees a sampler chain.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_sampler_free(IntPtr sampler);

    /// <summary>
    /// Initializes greedy sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_greedy();

    /// <summary>
    /// Initializes distribution sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_dist(uint seed);

    /// <summary>
    /// Initializes top-k sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_top_k(int k);

    /// <summary>
    /// Initializes top-p sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_top_p(float p, nuint minKeep);

    /// <summary>
    /// Initializes min-p sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_min_p(float p, nuint minKeep);

    /// <summary>
    /// Initializes temperature sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_temp(float t);

    /// <summary>
    /// Initializes mirostat sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_mirostat(
        int nVocab,
        uint seed,
        float tau,
        float eta);

    /// <summary>
    /// Initializes mirostat v2 sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_mirostat_v2(
        uint seed,
        float tau,
        float eta);

    /// <summary>
    /// Initializes penalties sampler.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_sampler_init_penalties(
        int nVocab,
        int specialEosId,
        int linefeedId,
        int penaltyLastN,
        float penaltyRepeat,
        float penaltyFreq,
        float penaltyPresent,
        [MarshalAs(UnmanagedType.I1)] bool penalizeNl,
        [MarshalAs(UnmanagedType.I1)] bool ignoreEos,
        float softmaxTemp,
        int nLogitsToKeep);

    /// <summary>
    /// Samples a token.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_sampler_sample(IntPtr sampler, IntPtr ctx, int idx);

    /// <summary>
    /// Gets sampler seed.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint llama_sampler_get_seed(IntPtr sampler);

    #endregion

    #region Utilities

    /// <summary>
    /// Gets llama.cpp version string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_version();

    /// <summary>
    /// Gets current time in microseconds.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long llama_time_us();

    /// <summary>
    /// Gets maximum number of devices.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint llama_max_devices();

    /// <summary>
    /// Gets maximum parallel sequences.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint llama_max_parallel_sequences();

    /// <summary>
    /// Checks if mmap is supported.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_supports_mmap();

    /// <summary>
    /// Checks if mlock is supported.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_supports_mlock();

    /// <summary>
    /// Checks if GPU offload is supported.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool llama_supports_gpu_offload();

    /// <summary>
    /// Gets system info string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_print_system_info();

    /// <summary>
    /// Gets ftype name.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_ftype_name(LlamaFType ftype);

    /// <summary>
    /// Gets flash attention type name.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_flash_attn_type_name(LlamaFlashAttnType flashAttnType);

    /// <summary>
    /// Gets load mode name.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_load_mode_name(LlamaLoadMode loadMode);

    /// <summary>
    /// Parses load mode from string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern LlamaLoadMode llama_load_mode_from_str(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    #endregion

    #region Performance

    /// <summary>
    /// Gets performance context data.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaPerfContextData llama_perf_context(IntPtr ctx);

    /// <summary>
    /// Prints performance context data.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_perf_context_print(IntPtr ctx);

    /// <summary>
    /// Resets performance context data.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_perf_context_reset(IntPtr ctx);

    #endregion
}

/// <summary>
/// Performance metrics for context operations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaPerfContextData
{
    public double TStartMs;
    public double TSampleMs;
    public double TPromptMs;
    public double TEvalMs;
    public long NSample;
    public long NPrompt;
    public long NEval;
    public long NDecoded;
}
