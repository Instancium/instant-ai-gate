using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Native bindings for libmtmd (multimodal support in llama.cpp)[cite: 1].
    /// </summary>
    public static partial class NativeMethods
    {
        private const string LibName = "mtmd";

        public enum MtmdInputChunkType : int
        {
            Text = 0,
            Image = 1,
            Audio = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdContextParams
        {
            [MarshalAs(UnmanagedType.I1)] public bool UseGpu;
            [MarshalAs(UnmanagedType.I1)] public bool PrintTimings;
            public int NThreads;
            public IntPtr ImageMarker;
            public IntPtr MediaMarker;
            public int FlashAttnType;
            [MarshalAs(UnmanagedType.I1)] public bool Warmup;
            public int ImageMinTokens;
            public int ImageMaxTokens;
            public IntPtr CbEval;
            public IntPtr CbEvalUserData;
            public int BatchMaxTokens;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdInputText
        {
            public IntPtr Text;
            [MarshalAs(UnmanagedType.I1)] public bool AddSpecial;
            [MarshalAs(UnmanagedType.I1)] public bool ParseSpecial;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdDecoderPos
        {
            public uint T;
            public uint X;
            public uint Y;
            public uint Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdCaps
        {
            [MarshalAs(UnmanagedType.I1)] public bool InpVision;
            [MarshalAs(UnmanagedType.I1)] public bool InpAudio;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int MtmdBitmapLazyCallback(
            nuint chunkIdx,
            IntPtr userData,
            out IntPtr outBitmap,
            out IntPtr outText);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GgmlLogCallback(int level, IntPtr text, IntPtr userData);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_default_marker")]
        public static extern IntPtr GetDefaultMarker();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_context_params_default")]
        public static extern MtmdContextParams GetDefaultContextParams();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_init_from_file", CharSet = CharSet.Ansi)]
        public static extern IntPtr InitFromFile(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string mmprojFname,
            IntPtr textModel,
            MtmdContextParams ctxParams);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_free")]
        public static extern void Free(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_decode_use_non_causal")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DecodeUseNonCausal(IntPtr ctx, IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_decode_use_mrope")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DecodeUseMrope(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_support_vision")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SupportVision(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_support_audio")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SupportAudio(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_audio_sample_rate")]
        public static extern int GetAudioSampleRate(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_marker")]
        public static extern IntPtr GetMarker(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init")]
        public static extern IntPtr BitmapInit(uint nx, uint ny, IntPtr data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init_from_audio")]
        public static extern IntPtr BitmapInitFromAudio(nuint nSamples, IntPtr data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_nx")]
        public static extern uint BitmapGetNx(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_ny")]
        public static extern uint BitmapGetNy(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_data")]
        public static extern IntPtr BitmapGetData(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_n_bytes")]
        public static extern nuint BitmapGetNBytes(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_is_audio")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool BitmapIsAudio(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_free")]
        public static extern void BitmapFree(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_id")]
        public static extern IntPtr BitmapGetId(IntPtr bitmap);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_set_id", CharSet = CharSet.Ansi)]
        public static extern void BitmapSetId(IntPtr bitmap, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init_lazy", CharSet = CharSet.Ansi)]
        public static extern IntPtr BitmapInitLazy(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            IntPtr userData,
            MtmdBitmapLazyCallback callback);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_init")]
        public static extern IntPtr InputChunksInit();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_size")]
        public static extern nuint InputChunksSize(IntPtr chunks);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_get")]
        public static extern IntPtr InputChunksGet(IntPtr chunks, nuint idx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_free")]
        public static extern void InputChunksFree(IntPtr chunks);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_type")]
        public static extern MtmdInputChunkType InputChunkGetType(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_tokens_text")]
        public static extern IntPtr InputChunkGetTokensText(IntPtr chunk, out nuint nTokensOutput);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_tokens_image")]
        public static extern IntPtr InputChunkGetTokensImage(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_n_tokens")]
        public static extern nuint InputChunkGetNTokens(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_id")]
        public static extern IntPtr InputChunkGetId(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_n_pos")]
        public static extern int InputChunkGetNPos(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_copy")]
        public static extern IntPtr InputChunkCopy(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_free")]
        public static extern void InputChunkFree(IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_image_tokens_get_n_tokens")]
        public static extern nuint ImageTokensGetNTokens(IntPtr imageTokens);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_image_tokens_get_id")]
        public static extern IntPtr ImageTokensGetId(IntPtr imageTokens);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_image_tokens_get_n_pos")]
        public static extern int ImageTokensGetNPos(IntPtr imageTokens);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_image_tokens_get_decoder_pos")]
        public static extern MtmdDecoderPos ImageTokensGetDecoderPos(IntPtr imageTokens, int pos0, nuint i);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_tokenize")]
        public static extern int Tokenize(
            IntPtr ctx,
            IntPtr output,
            ref MtmdInputText text,
            IntPtr[] bitmaps,
            nuint nBitmaps);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_encode_chunk")]
        public static extern int EncodeChunk(IntPtr ctx, IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_output_embd")]
        public static extern IntPtr GetOutputEmbd(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_init")]
        public static extern IntPtr BatchInit(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_free")]
        public static extern void BatchFree(IntPtr batch);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_add_chunk")]
        public static extern int BatchAddChunk(IntPtr batch, IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_encode")]
        public static extern int BatchEncode(IntPtr batch);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_get_output_embd")]
        public static extern IntPtr BatchGetOutputEmbd(IntPtr batch, IntPtr chunk);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_log_set")]
        public static extern void LogSet(GgmlLogCallback logCallback, IntPtr userData);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_cap_from_file", CharSet = CharSet.Ansi)]
        public static extern MtmdCaps GetCapFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string mmprojFname);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_test_create_input_chunks")]
        public static extern IntPtr TestCreateInputChunks();
    }
}