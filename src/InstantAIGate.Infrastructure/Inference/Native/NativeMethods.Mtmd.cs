using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    public static partial class NativeMethods
    {
        // ==========================================
        // MULTIMODAL (mtmd.h)
        // ==========================================

        public enum mtmd_input_chunk_type : int
        {
            MTMD_INPUT_CHUNK_TYPE_TEXT = 0,
            MTMD_INPUT_CHUNK_TYPE_IMAGE = 1,
            MTMD_INPUT_CHUNK_TYPE_AUDIO = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mtmd_context_params
        {
            [MarshalAs(UnmanagedType.I1)] public bool use_gpu;
            [MarshalAs(UnmanagedType.I1)] public bool print_timings;
            public int n_threads;

            public IntPtr image_marker; // const char * (deprecated)
            public IntPtr media_marker; // const char *

            public llama_flash_attn_type flash_attn_type;
            [MarshalAs(UnmanagedType.I1)] public bool warmup;

            public int image_min_tokens;
            public int image_max_tokens;

            public IntPtr cb_eval;
            public IntPtr cb_eval_user_data;

            public int batch_max_tokens;

            public IntPtr progress_callback;
            public IntPtr progress_callback_user_data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mtmd_input_text
        {
            public IntPtr text; // const char *
            [MarshalAs(UnmanagedType.I1)] public bool add_special;
            [MarshalAs(UnmanagedType.I1)] public bool parse_special;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mtmd_caps
        {
            [MarshalAs(UnmanagedType.I1)] public bool inp_vision;
            [MarshalAs(UnmanagedType.I1)] public bool inp_audio;
        }

        // --- Core Lifecycle ---

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_context_params_default")]
        public static extern mtmd_context_params mtmd_context_params_default();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_init_from_file", CharSet = CharSet.Ansi)]
        public static extern IntPtr mtmd_init_from_file(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string mmproj_fname,
            IntPtr text_model, // const struct llama_model *
            mtmd_context_params ctx_params);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_free")]
        public static extern void mtmd_free(IntPtr ctx);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_cap_from_file", CharSet = CharSet.Ansi)]
        public static extern mtmd_caps mtmd_get_cap_from_file([MarshalAs(UnmanagedType.LPUTF8Str)] string mmproj_fname);

        // --- Bitmap (Image/Audio) Management ---

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init")]
        public static extern IntPtr mtmd_bitmap_init(uint nx, uint ny, IntPtr data); // data is const unsigned char *

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_free")]
        public static extern void mtmd_bitmap_free(IntPtr bitmap);

        // --- Chunks Management ---

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_init")]
        public static extern IntPtr mtmd_input_chunks_init();

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_free")]
        public static extern void mtmd_input_chunks_free(IntPtr chunks);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_n_tokens")]
        public static extern nuint mtmd_input_chunk_get_n_tokens(IntPtr chunk);

        // --- Inference Pipeline ---

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_tokenize")]
        public static extern int mtmd_tokenize(
            IntPtr ctx,
            IntPtr output_chunks,
            ref mtmd_input_text text,
            IntPtr[] bitmaps, // Array of mtmd_bitmap pointers
            nuint n_bitmaps);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_encode_chunk")]
        public static extern int mtmd_encode_chunk(IntPtr ctx, IntPtr chunk);

        [DllImport("llama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_output_embd")]
        public static extern IntPtr mtmd_get_output_embd(IntPtr ctx); // Returns float*
    }
}