using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Provides native C# bindings for the libmtmd library, which enables multimodal support 
    /// (vision and audio processing) within the llama.cpp ecosystem.
    /// </summary>
    public static partial class NativeMtmdMethods 
    {
        private const string LibName = "mtmd";

        /// <summary> Defines the type of media chunk: Text, Image, or Audio. </summary>
        public enum MtmdInputChunkType : int
        {
            Text = 0,
            Image = 1,
            Audio = 2,
        }

        /// <summary>
        /// Configuration parameters for the multimodal context, controlling hardware acceleration, 
        /// tokenization behavior, and resource limits.
        /// <para>Use <see cref="GetDefaultContextParams"/> to initialize with standard values before overriding specific fields.</para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdContextParams
        {
            /// <summary>
            /// Enables GPU acceleration via ggml-backend for media encoding. 
            /// Set to false to force CPU-only processing.
            /// </summary>
            [MarshalAs(UnmanagedType.I1)] public bool UseGpu;

            /// <summary>
            /// If true, prints detailed timing information for each encoding step to the log.
            /// Useful for performance profiling and debugging latency issues.
            /// </summary>
            [MarshalAs(UnmanagedType.I1)] public bool PrintTimings;

            /// <summary>
            /// Number of CPU threads used for image/audio preprocessing (resizing, normalization).
            /// Does not affect the main inference thread count of the LLM.
            /// </summary>
            public int NThreads;

            /// <summary>
            /// [DEPRECATED] Legacy placeholder string for images. Use <see cref="MediaMarker"/> instead.
            /// </summary>
            public IntPtr ImageMarker;

            /// <summary>
            /// The string marker (e.g., "&lt;__media__&gt;") used in the text prompt to indicate 
            /// where an image or audio chunk should be inserted during tokenization.
            /// </summary>
            public IntPtr MediaMarker;

            /// <summary>
            /// Specifies the Flash Attention implementation type. 
            /// Improves speed and reduces memory usage for long contexts on supported hardware.
            /// </summary>
            public int FlashAttnType;

            /// <summary>
            /// Performs a "warmup" encoding pass immediately after initialization to stabilize 
            /// first-run performance by pre-loading GPU kernels and warming up caches.
            /// </summary>
            [MarshalAs(UnmanagedType.I1)] public bool Warmup;

            /// <summary>
            /// Minimum number of tokens reserved for a single image input. 
            /// Used primarily by models with dynamic resolution (e.g., Qwen-VL) to ensure consistent memory allocation.
            /// </summary>
            public int ImageMinTokens;

            /// <summary>
            /// Maximum number of tokens allowed for a single image input. 
            /// Limits the computational cost of processing very high-resolution images.
            /// </summary>
            public int ImageMaxTokens;

            /// <summary>
            /// Pointer to a custom callback function invoked during the evaluation of the ggml computation graph.
            /// Allows for advanced monitoring or modification of internal tensor operations.
            /// </summary>
            public IntPtr CbEval;

            /// <summary>
            /// User-defined data pointer passed as an argument to the <see cref="CbEval"/> callback.
            /// </summary>
            public IntPtr CbEvalUserData;

            /// <summary>
            /// Soft limit for the total number of output tokens in a single batch encoding operation.
            /// Note: This is not a hard constraint; the first media chunk will always be added even if it exceeds this limit.
            /// </summary>
            public int BatchMaxTokens;
        }

        /// <summary> Encapsulates raw text input to be tokenized, including flags for special token processing. </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdInputText
        {
            public IntPtr Text;
            [MarshalAs(UnmanagedType.I1)] public bool AddSpecial;
            [MarshalAs(UnmanagedType.I1)] public bool ParseSpecial;
        }

        /// <summary> Represents positional metadata for decoder attention, essential for M-RoPE calculations. </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdDecoderPos
        {
            public uint T;
            public uint X;
            public uint Y;
            public uint Z;
        }

        /// <summary> Indicates whether the loaded model supports vision or audio inputs. </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MtmdCaps
        {
            [MarshalAs(UnmanagedType.I1)] public bool InpVision;
            [MarshalAs(UnmanagedType.I1)] public bool InpAudio;
        }

        /// <summary> Callback delegate for lazy-loading media data (e.g., streaming video frames) 
        /// to optimize memory usage by avoiding loading entire files. </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int MtmdBitmapLazyCallback(
            nuint chunkIdx,
            IntPtr userData,
            out IntPtr outBitmap,
            out IntPtr outText);

        /// <summary> Delegate for routing internal library logs to a C# handler. </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GgmlLogCallback(int level, IntPtr text, IntPtr userData);

        /// <summary> Retrieves the default placeholder string (e.g., "&lt;__media__&gt;") used to 
        /// identify where media content should be inserted within a text prompt. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_default_marker")]
        public static extern IntPtr GetDefaultMarker();

        /// <summary> Returns a structure pre-populated with standard default configuration 
        /// parameters for the library. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_context_params_default")]
        public static extern MtmdContextParams GetDefaultContextParams();

        /// <summary> Initializes the multimodal context by loading the mmproj projection file 
        /// and binding it to an existing llama model. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_init_from_file", CharSet = CharSet.Ansi)]
        public static extern IntPtr InitFromFile(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string mmprojFname,
            IntPtr textModel,
            MtmdContextParams ctxParams);

        /// <summary> Cleans up and releases all resources associated with the context pointer. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_free")]
        public static extern void Free(IntPtr ctx);

        /// <summary> Determines if a specific chunk requires a non-causal attention mask, 
        /// typically used for image embeddings. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_decode_use_non_causal")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DecodeUseNonCausal(IntPtr ctx, IntPtr chunk);

        /// <summary> Checks if the model uses Rotary Positional Embeddings (M-RoPE) 
        /// for decoding. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_decode_use_mrope")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DecodeUseMrope(IntPtr ctx);

        /// <summary> Queries if the currently loaded model capability includes vision support. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_support_vision")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SupportVision(IntPtr ctx);

        /// <summary> Queries if the currently loaded model capability includes audio support. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_support_audio")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SupportAudio(IntPtr ctx);

        /// <summary> Returns the required audio sample rate (e.g., 16kHz for Whisper) 
        /// supported by the context. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_audio_sample_rate")]
        public static extern int GetAudioSampleRate(IntPtr ctx);

        /// <summary> Gets the actual marker string currently in use by the context. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_marker")]
        public static extern IntPtr GetMarker(IntPtr ctx);

        /// <summary> Allocates and initializes an image bitmap structure from raw RGB data. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init")]
        public static extern IntPtr BitmapInit(uint nx, uint ny, IntPtr data);

        /// <summary> Allocates and initializes a bitmap structure from floating-point PCM audio data. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init_from_audio")]
        public static extern IntPtr BitmapInitFromAudio(nuint nSamples, IntPtr data);

        /// <summary> Retrieves the width (nx) of the specified bitmap. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_nx")]
        public static extern uint BitmapGetNx(IntPtr bitmap);

        /// <summary> Retrieves the height (ny) of the specified bitmap. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_ny")]
        public static extern uint BitmapGetNy(IntPtr bitmap);

        /// <summary> Returns a direct pointer to the underlying raw media data. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_data")]
        public static extern IntPtr BitmapGetData(IntPtr bitmap);

        /// <summary> Returns the total byte count of the bitmap data. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_n_bytes")]
        public static extern nuint BitmapGetNBytes(IntPtr bitmap);

        /// <summary> Checks if the bitmap object contains audio samples. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_is_audio")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool BitmapIsAudio(IntPtr bitmap);

        /// <summary> Properly deallocates bitmap resources. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_free")]
        public static extern void BitmapFree(IntPtr bitmap);

        /// <summary> Retrieves the unique identifier associated with the bitmap. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_get_id")]
        public static extern IntPtr BitmapGetId(IntPtr bitmap);

        /// <summary> Assigns a unique string ID to a bitmap, often used for tracking in the KV cache. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_set_id", CharSet = CharSet.Ansi)]
        public static extern void BitmapSetId(IntPtr bitmap, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        /// <summary> Sets up a lazy bitmap using a callback; this is crucial for processing long-form 
        /// video by reading frames on-demand instead of loading the whole video. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_bitmap_init_lazy", CharSet = CharSet.Ansi)]
        public static extern IntPtr BitmapInitLazy(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            IntPtr userData,
            MtmdBitmapLazyCallback callback);

        /// <summary> Creates a new, empty list to hold input chunks. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_init")]
        public static extern IntPtr InputChunksInit();

        /// <summary> Returns the number of chunks currently held in the list. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_size")]
        public static extern nuint InputChunksSize(IntPtr chunks);

        /// <summary> Retrieves an individual chunk from the collection by its index. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_get")]
        public static extern IntPtr InputChunksGet(IntPtr chunks, nuint idx);

        /// <summary> Deallocates the input chunks collection and its contents. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunks_free")]
        public static extern void InputChunksFree(IntPtr chunks);

        /// <summary> Returns the classification (Text/Image/Audio) of a specific input chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_type")]
        public static extern MtmdInputChunkType InputChunkGetType(IntPtr chunk);

        /// <summary> Extracts the token list from a text-based chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_tokens_text")]
        public static extern IntPtr InputChunkGetTokensText(IntPtr chunk, out nuint nTokensOutput);

        /// <summary> Extracts image tokens from an image-based chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_tokens_image")]
        public static extern IntPtr InputChunkGetTokensImage(IntPtr chunk);

        /// <summary> Returns the count of tokens contained within a given chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_n_tokens")]
        public static extern nuint InputChunkGetNTokens(IntPtr chunk);

        /// <summary> Retrieves the ID associated with an input chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_id")]
        public static extern IntPtr InputChunkGetId(IntPtr chunk);

        /// <summary> Gets the temporal position count for the chunk (used for M-RoPE models). </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_get_n_pos")]
        public static extern int InputChunkGetNPos(IntPtr chunk);

        /// <summary> Creates a deep copy of a chunk, allowing the caller to manage its lifecycle independently. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_copy")]
        public static extern IntPtr InputChunkCopy(IntPtr chunk);

        /// <summary> Deallocates a single input chunk. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_input_chunk_free")]
        public static extern void InputChunkFree(IntPtr chunk);

        /// <summary> Tokenizes an input prompt by parsing markers and converting associated bitmaps 
        /// into an ordered sequence of input chunks. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_tokenize")]
        public static extern int Tokenize(
            IntPtr ctx,
            IntPtr output,
            ref MtmdInputText text,
            IntPtr[] bitmaps,
            nuint nBitmaps);

        /// <summary> Processes and encodes a single media chunk into the inference pipeline. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_encode_chunk")]
        public static extern int EncodeChunk(IntPtr ctx, IntPtr chunk);

        /// <summary> Retrieves the resulting embedding vectors generated from the last encode pass. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_output_embd")]
        public static extern IntPtr GetOutputEmbd(IntPtr ctx);

        /// <summary> Creates a new batch object to aggregate and process multiple chunks simultaneously. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_init")]
        public static extern IntPtr BatchInit(IntPtr ctx);

        /// <summary> Frees the batch object and its associated resources. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_free")]
        public static extern void BatchFree(IntPtr batch);

        /// <summary> Registers a chunk for batch processing. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_add_chunk")]
        public static extern int BatchAddChunk(IntPtr batch, IntPtr chunk);

        /// <summary> Executes the encoding process for the entire collection of added chunks. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_encode")]
        public static extern int BatchEncode(IntPtr batch);

        /// <summary> Configures the global logging callback for the library. </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_log_set")]
        public static extern void LogSet(GgmlLogCallback logCallback, IntPtr userData);

        /// <summary>
        /// Retrieves the capabilities of a projection file (mmproj) without requiring the initialization 
        /// of the full multimodal context, which is useful for checking model support at runtime.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_get_cap_from_file", CharSet = CharSet.Ansi)]
        public static extern MtmdCaps GetCapFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string mmprojFname);

        /// <summary>
        /// Retrieves the resulting output embeddings for a specific chunk after the batch encoding 
        /// process has been completed.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_batch_get_output_embd")]
        public static extern IntPtr BatchGetOutputEmbd(IntPtr batch, IntPtr chunk);

        /// <summary>
        /// Gets position for decoder attention, to be used by M-RoPE models.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mtmd_image_tokens_get_decoder_pos")]
        public static extern MtmdDecoderPos ImageTokensGetDecoderPos(IntPtr imageTokens, int pos0, nuint i);
    }
}