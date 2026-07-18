using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// P/Invoke bindings for llama.cpp and ggml.
    /// Based on llama.h and ggml.h (build b9631).
    /// </summary>
    internal static partial class NativeLlamaMethods
    {
        // ==========================================
        // 1. ENUMS (ggml.h & llama.h)
        // ==========================================

        /// <summary>
        /// Log levels as defined in ggml.h.
        /// IMPORTANT: Order is DEBUG=1, INFO=2, WARN=3, ERROR=4 (not alphabetical!)
        /// </summary>
        public enum ggml_log_level : int
        {
            GGML_LOG_LEVEL_NONE = 0,
            GGML_LOG_LEVEL_DEBUG = 1,
            GGML_LOG_LEVEL_INFO = 2,
            GGML_LOG_LEVEL_WARN = 3,
            GGML_LOG_LEVEL_ERROR = 4,
            GGML_LOG_LEVEL_CONT = 5  // continue previous log
        }

        public enum ggml_type : int
        {
            GGML_TYPE_F32 = 0,
            GGML_TYPE_F16 = 1,
            GGML_TYPE_Q4_0 = 2,
            GGML_TYPE_Q4_1 = 3,
            GGML_TYPE_Q5_0 = 6,
            GGML_TYPE_Q5_1 = 7,
            GGML_TYPE_Q8_0 = 8,
            GGML_TYPE_Q8_1 = 9,
            GGML_TYPE_Q2_K = 10,
            GGML_TYPE_Q3_K = 11,
            GGML_TYPE_Q4_K = 12,
            GGML_TYPE_Q5_K = 13,
            GGML_TYPE_Q6_K = 14,
            GGML_TYPE_IQ2_XXS = 15,
            GGML_TYPE_IQ2_XS = 16,
            GGML_TYPE_IQ3_XXS = 17,
            GGML_TYPE_IQ1_S = 18,
            GGML_TYPE_IQ4_NL = 19,
            GGML_TYPE_IQ3_S = 20,
            GGML_TYPE_IQ2_S = 21,
            GGML_TYPE_IQ4_XS = 22,
            GGML_TYPE_I8 = 23,
            GGML_TYPE_I16 = 24,
            GGML_TYPE_I32 = 25,
            GGML_TYPE_I64 = 26,
            GGML_TYPE_F64 = 27,
            GGML_TYPE_IQ1_M = 28,
            GGML_TYPE_BF16 = 29,
        }

        public enum llama_flash_attn_type : int
        {
            LLAMA_FLASH_ATTN_TYPE_AUTO = -1,
            LLAMA_FLASH_ATTN_TYPE_DISABLED = 0,
            LLAMA_FLASH_ATTN_TYPE_ENABLED = 1,
        }

        public enum llama_split_mode : int
        {
            LLAMA_SPLIT_MODE_NONE = 0,
            LLAMA_SPLIT_MODE_LAYER = 1,
            LLAMA_SPLIT_MODE_ROW = 2,
            LLAMA_SPLIT_MODE_TENSOR = 3,
        }

        public enum llama_context_type : int
        {
            LLAMA_CONTEXT_TYPE_DEFAULT = 0,
            LLAMA_CONTEXT_TYPE_MTP = 1,
        }

        public enum llama_rope_scaling_type : int
        {
            LLAMA_ROPE_SCALING_TYPE_UNSPECIFIED = -1,
            LLAMA_ROPE_SCALING_TYPE_NONE = 0,
            LLAMA_ROPE_SCALING_TYPE_LINEAR = 1,
            LLAMA_ROPE_SCALING_TYPE_YARN = 2,
            LLAMA_ROPE_SCALING_TYPE_LONGROPE = 3,
        }

        public enum llama_pooling_type : int
        {
            LLAMA_POOLING_TYPE_UNSPECIFIED = -1,
            LLAMA_POOLING_TYPE_NONE = 0,
            LLAMA_POOLING_TYPE_MEAN = 1,
            LLAMA_POOLING_TYPE_CLS = 2,
            LLAMA_POOLING_TYPE_LAST = 3,
            LLAMA_POOLING_TYPE_RANK = 4,
        }

        public enum llama_attention_type : int
        {
            LLAMA_ATTENTION_TYPE_UNSPECIFIED = -1,
            LLAMA_ATTENTION_TYPE_CAUSAL = 0,
            LLAMA_ATTENTION_TYPE_NON_CAUSAL = 1,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ggml_log_callback(ggml_log_level level, IntPtr text, IntPtr user_data);

        // ==========================================
        // 2. LOGGING (ggml.h)
        // ==========================================

        // ggml_log_set is in ggml.dll (or ggml-base.dll in some builds)
        [DllImport("ggml", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_log_set")]
        public static extern void ggml_log_set(ggml_log_callback log_callback, IntPtr user_data);

        // llama_log_set is in llama.dll (wrapper around ggml_log_set)
        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_log_set")]
        public static extern void llama_log_set(ggml_log_callback log_callback, IntPtr user_data);

        // ==========================================
        // 3. BACKEND LOADING (ggml-backend.h)
        // ==========================================

        [DllImport("ggml", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_load_all")]
        public static extern void ggml_backend_load_all_ggml();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_load_all")]
        public static extern void ggml_backend_load_all_llama();

        // ==========================================
        // 4. BACKEND LIFECYCLE (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_backend_init")]
        public static extern void llama_backend_init();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_backend_free")]
        public static extern void llama_backend_free();

        // ==========================================
        // 5. SYSTEM INFO (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_print_system_info")]
        public static extern IntPtr llama_print_system_info();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_supports_gpu_offload")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool llama_supports_gpu_offload();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_supports_mmap")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool llama_supports_mmap();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_supports_mlock")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool llama_supports_mlock();

        // ==========================================
        // 6. MODEL (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_default_params")]
        public static extern llama_model_params llama_model_default_params();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_load_from_file", CharSet = CharSet.Ansi)]
        public static extern IntPtr llama_model_load_from_file(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path_model,
            llama_model_params @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_free")]
        public static extern void llama_model_free(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_get_vocab")]
        public static extern IntPtr llama_model_get_vocab(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_embd")]
        public static extern int llama_model_n_embd(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_ctx_train")]
        public static extern int llama_model_n_ctx_train(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_layer")]
        public static extern int llama_model_n_layer(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_head")]
        public static extern int llama_model_n_head(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_desc")]
        public static extern int llama_model_desc(IntPtr model, [Out] byte[] buf, int buf_size);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_size")]
        public static extern ulong llama_model_size(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_params")]
        public static extern ulong llama_model_n_params(IntPtr model);

        // ==========================================
        // 7. CONTEXT (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_context_default_params")]
        public static extern llama_context_params llama_context_default_params();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_init_from_model")]
        public static extern IntPtr llama_init_from_model(
            IntPtr model,
            llama_context_params @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_free")]
        public static extern void llama_free(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_memory")]
        public static extern IntPtr llama_get_memory(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_memory_clear")]
        public static extern void llama_memory_clear(IntPtr mem, [MarshalAs(UnmanagedType.I1)] bool data);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_n_ctx")]
        public static extern uint llama_n_ctx(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_n_batch")]
        public static extern uint llama_n_batch(IntPtr ctx);

        // ==========================================
        // 8. VOCABULARY & TOKENIZATION (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_n_tokens")]
        public static extern int llama_vocab_n_tokens(IntPtr vocab);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_tokenize")]
        public static extern int llama_tokenize(
            IntPtr vocab,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            int text_len,
            [In, Out] int[] tokens,
            int n_tokens_max,
            [MarshalAs(UnmanagedType.I1)] bool add_special,
            [MarshalAs(UnmanagedType.I1)] bool parse_special);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_token_to_piece")]
        public static extern int llama_token_to_piece(
            IntPtr vocab,
            int token,
            [Out] byte[] buf,
            int length,
            int lstrip,
            [MarshalAs(UnmanagedType.I1)] bool special);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_eos")]
        public static extern int llama_vocab_eos(IntPtr vocab);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_bos")]
        public static extern int llama_vocab_bos(IntPtr vocab);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_is_eog")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool llama_vocab_is_eog(IntPtr vocab, int token);

        // ==========================================
        // 9. BATCH & DECODE (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_decode")]
        public static extern int llama_decode(IntPtr ctx, LlamaBatch batch);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_batch_init")]
        public static extern LlamaBatch llama_batch_init(int n_tokens, int embd, int n_seq_max);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_batch_free")]
        public static extern void llama_batch_free(LlamaBatch batch);

        // ==========================================
        // 10. EMBEDDINGS & LOGITS (llama.h)
        // ==========================================

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_embeddings_ith")]
        public static extern IntPtr llama_get_embeddings_ith(IntPtr ctx, int i);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_embeddings_seq")]
        public static extern IntPtr llama_get_embeddings_seq(IntPtr ctx, int seq_id);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_logits_ith")]
        public static extern IntPtr llama_get_logits_ith(IntPtr ctx, int i);

        // ==========================================
        // 11. SAMPLERS (llama.h)
        // ==========================================

        // Sampler chain management
        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_default_params")]
        public static extern llama_sampler_chain_params llama_sampler_chain_default_params();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_init")]
        public static extern IntPtr llama_sampler_chain_init(llama_sampler_chain_params @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_add")]
        public static extern void llama_sampler_chain_add(IntPtr chain, IntPtr smpl);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_n")]
        public static extern int llama_sampler_chain_n(IntPtr chain);

        // Individual samplers
        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_greedy")]
        public static extern IntPtr llama_sampler_init_greedy();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_dist")]
        public static extern IntPtr llama_sampler_init_dist(uint seed);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_temp")]
        public static extern IntPtr llama_sampler_init_temp(float t);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_temp_ext")]
        public static extern IntPtr llama_sampler_init_temp_ext(float t, float delta, float exponent);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_k")]
        public static extern IntPtr llama_sampler_init_top_k(int k);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_p")]
        public static extern IntPtr llama_sampler_init_top_p(float p, nuint min_keep);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_min_p")]
        public static extern IntPtr llama_sampler_init_min_p(float p, nuint min_keep);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_typical")]
        public static extern IntPtr llama_sampler_init_typical(float p, nuint min_keep);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_penalties")]
        public static extern IntPtr llama_sampler_init_penalties(
            int penalty_last_n,
            float penalty_repeat,
            float penalty_freq,
            float penalty_present);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_mirostat_v2")]
        public static extern IntPtr llama_sampler_init_mirostat_v2(uint seed, float tau, float eta);

        // Sampler lifecycle
        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_sample")]
        public static extern int llama_sampler_sample(IntPtr smpl, IntPtr ctx, int idx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_accept")]
        public static extern void llama_sampler_accept(IntPtr smpl, int token);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_reset")]
        public static extern void llama_sampler_reset(IntPtr smpl);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_free")]
        public static extern void llama_sampler_free(IntPtr smpl);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_get_seed")]
        public static extern uint llama_sampler_get_seed(IntPtr smpl);




        // ==========================================
        // STRUCTS
        // ==========================================

        /// <summary>
        /// struct llama_model_params from llama.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct llama_model_params
        {
            public IntPtr devices;
            public IntPtr tensor_buft_overrides;
            public int n_gpu_layers;
            public llama_split_mode split_mode;
            public int main_gpu;
            public IntPtr tensor_split;
            public IntPtr progress_callback;
            public IntPtr progress_callback_user_data;
            public IntPtr kv_overrides;

            [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
            [MarshalAs(UnmanagedType.I1)] public bool use_mmap;
            [MarshalAs(UnmanagedType.I1)] public bool use_direct_io;
            [MarshalAs(UnmanagedType.I1)] public bool use_mlock;
            [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
            [MarshalAs(UnmanagedType.I1)] public bool use_extra_bufts;
            [MarshalAs(UnmanagedType.I1)] public bool no_host;
            [MarshalAs(UnmanagedType.I1)] public bool no_alloc;
        }

        /// <summary>
        /// struct llama_context_params from llama.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct llama_context_params
        {
            public uint n_ctx;
            public uint n_batch;
            public uint n_ubatch;
            public uint n_seq_max;
            public uint n_rs_seq;
            public uint n_outputs_max;
            public int n_threads;
            public int n_threads_batch;

            public llama_context_type ctx_type;
            public llama_rope_scaling_type rope_scaling_type;
            public llama_pooling_type pooling_type;
            public llama_attention_type attention_type;
            public llama_flash_attn_type flash_attn_type;

            public float rope_freq_base;
            public float rope_freq_scale;
            public float yarn_ext_factor;
            public float yarn_attn_factor;
            public float yarn_beta_fast;
            public float yarn_beta_slow;
            public uint yarn_orig_ctx;
            public float defrag_thold;

            public IntPtr cb_eval;
            public IntPtr cb_eval_user_data;

            public ggml_type type_k;
            public ggml_type type_v;

            public IntPtr abort_callback;
            public IntPtr abort_callback_data;

            [MarshalAs(UnmanagedType.I1)] public bool embeddings;
            [MarshalAs(UnmanagedType.I1)] public bool offload_kqv;
            [MarshalAs(UnmanagedType.I1)] public bool no_perf;
            [MarshalAs(UnmanagedType.I1)] public bool op_offload;
            [MarshalAs(UnmanagedType.I1)] public bool swa_full;
            [MarshalAs(UnmanagedType.I1)] public bool kv_unified;

            public IntPtr samplers;
            public nuint n_samplers;
            public IntPtr ctx_other;
        }

        /// <summary>
        /// struct llama_batch from llama.h.
        /// Note: logits is int8_t* (1 byte), NOT int32_t*.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct LlamaBatch
        {
            public int n_tokens;
            public IntPtr token;      // llama_token* (int32_t*)
            public IntPtr embd;       // float*
            public IntPtr pos;        // llama_pos* (int32_t*)
            public IntPtr n_seq_id;   // int32_t*
            public IntPtr seq_id;     // llama_seq_id** (int32_t**)
            public IntPtr logits;     // int8_t* — 1 byte per token
        }

        /// <summary>
        /// struct llama_sampler_chain_params from llama.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct llama_sampler_chain_params
        {
            [MarshalAs(UnmanagedType.I1)] public bool no_perf;
        }

    }

}