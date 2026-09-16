using System.Runtime.InteropServices;

namespace InstantAIGate.Native.Bindings;

/// <summary>
/// P/Invoke bindings for mtmd.h native functions.
/// </summary>
internal static partial class MtmdNative
{
    private const string LibraryName = "mtmd";

    #region Context Management

    /// <summary>
    /// Gets the default media marker string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_default_marker();

    /// <summary>
    /// Gets default context parameters.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern MtmdContextParams mtmd_context_params_default();

    /// <summary>
    /// Initializes mtmd context from a file.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr mtmd_init_from_file(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string mmprojFname,
        IntPtr textModel,
        in MtmdContextParams ctxParams);

    /// <summary>
    /// Frees mtmd context.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_free(IntPtr ctx);

    /// <summary>
    /// Checks if non-causal mask is needed for decoding.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mtmd_decode_use_non_causal(IntPtr ctx, IntPtr chunk);

    /// <summary>
    /// Checks if M-RoPE is used for decoding.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mtmd_decode_use_mrope(IntPtr ctx);

    /// <summary>
    /// Checks if vision input is supported.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mtmd_support_vision(IntPtr ctx);

    /// <summary>
    /// Checks if audio input is supported.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mtmd_support_audio(IntPtr ctx);

    /// <summary>
    /// Gets audio sample rate in Hz.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_get_audio_sample_rate(IntPtr ctx);

    /// <summary>
    /// Gets the current marker string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_get_marker(IntPtr ctx);

    #endregion

    #region Bitmap Management

    /// <summary>
    /// Initializes a bitmap from image data (RGB format).
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_bitmap_init(uint nx, uint ny, IntPtr data);

    /// <summary>
    /// Initializes a bitmap from audio data (float PCM).
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_bitmap_init_from_audio(nuint nSamples, IntPtr data);

    /// <summary>
    /// Gets bitmap width.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint mtmd_bitmap_get_nx(IntPtr bitmap);

    /// <summary>
    /// Gets bitmap height.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint mtmd_bitmap_get_ny(IntPtr bitmap);

    /// <summary>
    /// Gets bitmap data pointer.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_bitmap_get_data(IntPtr bitmap);

    /// <summary>
    /// Gets bitmap data size in bytes.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint mtmd_bitmap_get_n_bytes(IntPtr bitmap);

    /// <summary>
    /// Checks if bitmap contains audio data.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mtmd_bitmap_is_audio(IntPtr bitmap);

    /// <summary>
    /// Frees a bitmap.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_bitmap_free(IntPtr bitmap);

    /// <summary>
    /// Gets bitmap ID.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_bitmap_get_id(IntPtr bitmap);

    /// <summary>
    /// Sets bitmap ID.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void mtmd_bitmap_set_id(
        IntPtr bitmap,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

    /// <summary>
    /// Sets bitmap mergeable flag for video models.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_bitmap_set_mergeable(IntPtr bitmap, [MarshalAs(UnmanagedType.I1)] bool mergeable);

    /// <summary>
    /// Initializes a lazy bitmap with callback.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_bitmap_init_lazy(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        IntPtr userData,
        MtmdBitmapLazyCallback callback);

    #endregion

    #region Input Chunks

    /// <summary>
    /// Initializes input chunks collection.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunks_init();

    /// <summary>
    /// Gets number of chunks in collection.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint mtmd_input_chunks_size(IntPtr chunks);

    /// <summary>
    /// Gets chunk at index.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunks_get(IntPtr chunks, nuint idx);

    /// <summary>
    /// Frees input chunks collection.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_input_chunks_free(IntPtr chunks);

    /// <summary>
    /// Gets chunk type.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern MtmdInputChunkType mtmd_input_chunk_get_type(IntPtr chunk);

    /// <summary>
    /// Gets text tokens from chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunk_get_tokens_text(IntPtr chunk, out nuint nTokensOutput);

    /// <summary>
    /// Gets image tokens from chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunk_get_tokens_image(IntPtr chunk);

    /// <summary>
    /// Gets number of tokens in chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint mtmd_input_chunk_get_n_tokens(IntPtr chunk);

    /// <summary>
    /// Gets chunk ID.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunk_get_id(IntPtr chunk);

    /// <summary>
    /// Gets number of positions in chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_input_chunk_get_n_pos(IntPtr chunk);

    /// <summary>
    /// Copies an input chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunk_copy(IntPtr chunk);

    /// <summary>
    /// Frees an input chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_input_chunk_free(IntPtr chunk);

    /// <summary>
    /// Gets placeholder chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_input_chunk_get_placeholder(IntPtr chunk);

    /// <summary>
    /// Saves chunk metadata to buffer.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_input_chunk_save(
        IntPtr chunk,
        IntPtr outBuf,
        nuint outLen,
        out nuint expectedOutLen);

    /// <summary>
    /// Loads chunk metadata from buffer.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr mtmd_input_chunk_load(IntPtr buf, nuint len);

    #endregion

    #region Image Tokens

    /// <summary>
    /// Gets number of tokens in image tokens structure.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint mtmd_image_tokens_get_n_tokens(IntPtr imageTokens);

    /// <summary>
    /// Gets image tokens ID.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_image_tokens_get_id(IntPtr imageTokens);

    /// <summary>
    /// Gets number of positions in image tokens.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_image_tokens_get_n_pos(IntPtr imageTokens);

    /// <summary>
    /// Gets decoder position for M-RoPE models.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern MtmdDecoderPos mtmd_image_tokens_get_decoder_pos(
        IntPtr imageTokens,
        int pos0,
        nuint i);

    #endregion

    #region Tokenization

    /// <summary>
    /// Tokenizes text and bitmaps into chunks.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_tokenize(
        IntPtr ctx,
        IntPtr output,
        in MtmdInputText text,
        IntPtr[] bitmaps,
        nuint nBitmaps);

    /// <summary>
    /// Tokenizes input parts into chunks.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_tokenize_from_parts(
        IntPtr ctx,
        IntPtr output,
        IntPtr[] parts,
        nuint nParts,
        [MarshalAs(UnmanagedType.I1)] bool addSpecial);

    #endregion

    #region Encoding

    /// <summary>
    /// Encodes a single chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_encode_chunk(IntPtr ctx, IntPtr chunk);

    /// <summary>
    /// Gets output embeddings from last encode pass.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_get_output_embd(IntPtr ctx);

    #endregion

    #region Batch Encoding

    /// <summary>
    /// Initializes batch for encoding.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_batch_init(IntPtr ctx);

    /// <summary>
    /// Frees batch.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_batch_free(IntPtr batch);

    /// <summary>
    /// Adds chunk to batch.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_batch_add_chunk(IntPtr batch, IntPtr chunk);

    /// <summary>
    /// Encodes all chunks in batch.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mtmd_batch_encode(IntPtr batch);

    /// <summary>
    /// Gets output embeddings for a specific chunk.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mtmd_batch_get_output_embd(IntPtr batch, IntPtr chunk);

    #endregion

    #region Logging

    /// <summary>
    /// Sets logging callback.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mtmd_log_set(IntPtr logCallback, IntPtr userData);

    #endregion
}
