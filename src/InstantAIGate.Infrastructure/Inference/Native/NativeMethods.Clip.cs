using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    public static partial class NativeMethods
    {
        // ==========================================
        // 12. VISION / MTMD / CLIP (clip.h)
        // ==========================================

        public enum clip_modality : int
        {
            CLIP_MODALITY_VISION = 0,
            CLIP_MODALITY_AUDIO = 1
        }

        public enum clip_flash_attn_type : int
        {
            CLIP_FLASH_ATTN_TYPE_AUTO = -1,
            CLIP_FLASH_ATTN_TYPE_DISABLED = 0,
            CLIP_FLASH_ATTN_TYPE_ENABLED = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct clip_image_size
        {
            public int width;
            public int height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct clip_context_params
        {
            [MarshalAs(UnmanagedType.I1)] public bool use_gpu;
            public clip_flash_attn_type flash_attn_type;
            public int image_min_tokens;
            public int image_max_tokens;
            [MarshalAs(UnmanagedType.I1)] public bool warmup;
            public IntPtr cb_eval; // ggml_backend_sched_eval_callback
            public IntPtr cb_eval_user_data;
            [MarshalAs(UnmanagedType.I1)] public bool no_alloc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct clip_init_result
        {
            public IntPtr ctx_v; // vision context (struct clip_ctx *)
            public IntPtr ctx_a; // audio context (struct clip_ctx *)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct clip_cap
        {
            [MarshalAs(UnmanagedType.I1)] public bool has_vision;
            [MarshalAs(UnmanagedType.I1)] public bool has_audio;
        }

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_init", CharSet = CharSet.Ansi)]
        public static extern clip_init_result clip_init(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string fname,
            clip_context_params ctx_params);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_free")]
        public static extern void clip_free(IntPtr ctx);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_get_image_size")]
        public static extern int clip_get_image_size(IntPtr ctx);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_get_patch_size")]
        public static extern int clip_get_patch_size(IntPtr ctx);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_get_hidden_size")]
        public static extern int clip_get_hidden_size(IntPtr ctx);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_n_output_tokens")]
        public static extern int clip_n_output_tokens(IntPtr ctx, IntPtr img);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_n_mmproj_embd")]
        public static extern int clip_n_mmproj_embd(IntPtr ctx);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_image_f32_init")]
        public static extern IntPtr clip_image_f32_init();

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_image_f32_free")]
        public static extern void clip_image_f32_free(IntPtr img);

        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_get_cap", CharSet = CharSet.Ansi)]
        public static extern clip_cap clip_get_cap([MarshalAs(UnmanagedType.LPUTF8Str)] string fname);

        /// <summary>
        /// IMPORTANT: The native signature uses std::vector<float>& for out_vec.
        /// P/Invoke does not support C++ standard library containers. 
        /// This requires a C-ABI compatible wrapper function in the native library to work safely.
        /// Exposed here as IntPtr for architecture completeness.
        /// </summary>
        [DllImport("clip", CallingConvention = CallingConvention.Cdecl, EntryPoint = "clip_image_encode")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool clip_image_encode(IntPtr ctx, int n_threads, IntPtr img, IntPtr out_vec);
    }
}