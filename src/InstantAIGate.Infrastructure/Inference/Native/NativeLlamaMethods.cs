using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// P/Invoke bindings for llama.cpp and ggml.
    /// Uses EntryPoint to map PascalCase C# methods to snake_case native exports.
    /// </summary>
    public static partial class NativeLlamaMethods
    {
        public enum GgmlLogLevel : int
        {
            None = 0,
            Debug = 1,
            Info = 2,
            Warn = 3,
            Error = 4,
            Cont = 5
        }

        public enum GgmlType : int
        {
            F32 = 0, F16 = 1, Q4_0 = 2, Q4_1 = 3, Q5_0 = 6, Q5_1 = 7,
            Q8_0 = 8, Q8_1 = 9, Q2_K = 10, Q3_K = 11, Q4_K = 12, Q5_K = 13,
            Q6_K = 14, IQ2_XXS = 15, IQ2_XS = 16, IQ3_XXS = 17, IQ1_S = 18,
            IQ4_NL = 19, IQ3_S = 20, IQ2_S = 21, IQ4_XS = 22, I8 = 23,
            I16 = 24, I32 = 25, I64 = 26, F64 = 27, IQ1_M = 28, BF16 = 29,
        }

        public enum LlamaFlashAttnType : int
        {
            Auto = -1,
            Disabled = 0,
            Enabled = 1,
        }

        public enum LlamaSplitMode : int
        {
            None = 0,
            Layer = 1,
            Row = 2,
            Tensor = 3,
        }

        public enum LlamaContextType : int
        {
            Default = 0,
            Mtp = 1,
        }

        public enum LlamaRopeScalingType : int
        {
            Unspecified = -1,
            None = 0,
            Linear = 1,
            Yarn = 2,
            Longrope = 3,
        }

        public enum LlamaPoolingType : int
        {
            Unspecified = -1,
            None = 0,
            Mean = 1,
            Cls = 2,
            Last = 3,
            Rank = 4,
        }

        public enum LlamaAttentionType : int
        {
            Unspecified = -1,
            Causal = 0,
            NonCausal = 1,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GgmlLogCallback(GgmlLogLevel level, IntPtr text, IntPtr userData);

        [DllImport("ggml", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_log_set")]
        public static extern void GgmlLogSet(GgmlLogCallback logCallback, IntPtr userData);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_log_set")]
        public static extern void LlamaLogSet(GgmlLogCallback logCallback, IntPtr userData);

        [DllImport("ggml", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_load_all")]
        public static extern void GgmlBackendLoadAll();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_load_all")]
        public static extern void LlamaBackendLoadAll();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_backend_init")]
        public static extern void LlamaBackendInit();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_backend_free")]
        public static extern void LlamaBackendFree();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_supports_gpu_offload")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool LlamaSupportsGpuOffload();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_default_params")]
        public static extern LlamaModelParams LlamaModelDefaultParams();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_load_from_file", CharSet = CharSet.Ansi)]
        public static extern IntPtr LlamaModelLoadFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string pathModel, LlamaModelParams @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_free")]
        public static extern void LlamaModelFree(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_get_vocab")]
        public static extern IntPtr LlamaModelGetVocab(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_n_embd")]
        public static extern int LlamaModelNEmbd(IntPtr model);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_context_default_params")]
        public static extern LlamaContextParams LlamaContextDefaultParams();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_init_from_model")]
        public static extern IntPtr LlamaInitFromModel(IntPtr model, LlamaContextParams @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_free")]
        public static extern void LlamaFree(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_memory")]
        public static extern IntPtr LlamaGetMemory(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_memory_clear")]
        public static extern void LlamaMemoryClear(IntPtr mem, [MarshalAs(UnmanagedType.I1)] bool data);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_tokenize")]
        public static extern int LlamaTokenize(IntPtr vocab, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int textLen, [In, Out] int[] tokens, int nTokensMax, [MarshalAs(UnmanagedType.I1)] bool addSpecial, [MarshalAs(UnmanagedType.I1)] bool parseSpecial);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_token_to_piece")]
        public static extern int LlamaTokenToPiece(IntPtr vocab, int token, [Out] byte[] buf, int length, int lstrip, [MarshalAs(UnmanagedType.I1)] bool special);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_eos")]
        public static extern int LlamaVocabEos(IntPtr vocab);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_decode")]
        public static extern int LlamaDecode(IntPtr ctx, LlamaBatch batch);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_embeddings_ith")]
        public static extern IntPtr LlamaGetEmbeddingsIth(IntPtr ctx, int i);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_default_params")]
        public static extern LlamaSamplerChainParams LlamaSamplerChainDefaultParams();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_init")]
        public static extern IntPtr LlamaSamplerChainInit(LlamaSamplerChainParams @params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_add")]
        public static extern void LlamaSamplerChainAdd(IntPtr chain, IntPtr smpl);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_dist")]
        public static extern IntPtr LlamaSamplerInitDist(uint seed);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_temp")]
        public static extern IntPtr LlamaSamplerInitTemp(float t);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_k")]
        public static extern IntPtr LlamaSamplerInitTopK(int k);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_p")]
        public static extern IntPtr LlamaSamplerInitTopP(float p, nuint minKeep);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_penalties")]
        public static extern IntPtr LlamaSamplerInitPenalties(int penaltyLastN, float penaltyRepeat, float penaltyFreq, float penaltyPresent);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_sample")]
        public static extern int LlamaSamplerSample(IntPtr smpl, IntPtr ctx, int idx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_accept")]
        public static extern void LlamaSamplerAccept(IntPtr smpl, int token);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_free")]
        public static extern void LlamaSamplerFree(IntPtr smpl);

        [StructLayout(LayoutKind.Sequential)]
        public struct LlamaModelParams
        {
            public IntPtr Devices;
            public IntPtr TensorBuftOverrides;
            public int NGpuLayers;
            public LlamaSplitMode SplitMode;
            public int MainGpu;
            public IntPtr TensorSplit;
            public IntPtr ProgressCallback;
            public IntPtr ProgressCallbackUserData;
            public IntPtr KvOverrides;
            [MarshalAs(UnmanagedType.I1)] public bool VocabOnly;
            [MarshalAs(UnmanagedType.I1)] public bool UseMmap;
            [MarshalAs(UnmanagedType.I1)] public bool UseDirectIo;
            [MarshalAs(UnmanagedType.I1)] public bool UseMlock;
            [MarshalAs(UnmanagedType.I1)] public bool CheckTensors;
            [MarshalAs(UnmanagedType.I1)] public bool UseExtraBufts;
            [MarshalAs(UnmanagedType.I1)] public bool NoHost;
            [MarshalAs(UnmanagedType.I1)] public bool NoAlloc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LlamaContextParams
        {
            public uint NCtx;
            public uint NBatch;
            public uint NUBatch;
            public uint NSeqMax;
            public uint NRsSeq;
            public uint NOutputsMax;
            public int NThreads;
            public int NThreadsBatch;
            public LlamaContextType CtxType;
            public LlamaRopeScalingType RopeScalingType;
            public LlamaPoolingType PoolingType;
            public LlamaAttentionType AttentionType;
            public LlamaFlashAttnType FlashAttnType;
            public float RopeFreqBase;
            public float RopeFreqScale;
            public float YarnExtFactor;
            public float YarnAttnFactor;
            public float YarnBetaFast;
            public float YarnBetaSlow;
            public uint YarnOrigCtx;
            public float DefragThold;
            public IntPtr CbEval;
            public IntPtr CbEvalUserData;
            public GgmlType TypeK;
            public GgmlType TypeV;
            public IntPtr AbortCallback;
            public IntPtr AbortCallbackData;
            [MarshalAs(UnmanagedType.I1)] public bool Embeddings;
            [MarshalAs(UnmanagedType.I1)] public bool OffloadKqv;
            [MarshalAs(UnmanagedType.I1)] public bool NoPerf;
            [MarshalAs(UnmanagedType.I1)] public bool OpOffload;
            [MarshalAs(UnmanagedType.I1)] public bool SwaFull;
            [MarshalAs(UnmanagedType.I1)] public bool KvUnified;
            public IntPtr Samplers;
            public nuint NSamplers;
            public IntPtr CtxOther;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LlamaBatch
        {
            public int NTokens;
            public IntPtr Token;
            public IntPtr Embd;
            public IntPtr Pos;
            public IntPtr NSeqId;
            public IntPtr SeqId;
            public IntPtr Logits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LlamaSamplerChainParams
        {
            [MarshalAs(UnmanagedType.I1)] public bool NoPerf;
        }
    }
}