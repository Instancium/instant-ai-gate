using InstantAIGate.Native.Bindings;
using System.Runtime.InteropServices;

namespace InstantAIGate.Native.Core;

/// <summary>
/// High-level wrapper for multimodal (mtmd) context management.
/// </summary>
public sealed class MultiModalContext : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the context is initialized.
    /// </summary>
    public bool IsInitialized => _handle != IntPtr.Zero && !_disposed;

    /// <summary>
    /// Gets a value indicating whether vision input is supported.
    /// </summary>
    public bool SupportsVision { get; private set; }

    /// <summary>
    /// Gets a value indicating whether audio input is supported.
    /// </summary>
    public bool SupportsAudio { get; private set; }

    /// <summary>
    /// Gets the audio sample rate in Hz (for audio models).
    /// </summary>
    public int AudioSampleRate { get; private set; }

    /// <summary>
    /// Gets the current media marker string.
    /// </summary>
    public string? MediaMarker { get; private set; }

    /// <summary>
    /// Gets a value indicating whether M-RoPE is used for decoding.
    /// </summary>
    public bool UsesMRope { get; private set; }

    /// <summary>
    /// Gets a value indicating whether non-causal mask is needed for decoding.
    /// </summary>
    public bool UsesNonCausalMask { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiModalContext"/> class.
    /// </summary>
    public MultiModalContext()
    {
        _handle = IntPtr.Zero;
    }

    /// <summary>
    /// Initializes the multimodal context from a projector file.
    /// </summary>
    /// <param name="projectorPath">Path to the mmproj model file.</param>
    /// <param name="textModel">The text model to use with the projector.</param>
    /// <param name="parameters">Context parameters.</param>
    /// <returns><see langword="true"/> if initialization was successful.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public bool Initialize(string projectorPath, LlamaModel textModel, MtmdContextParams? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!textModel.IsLoaded)
        {
            throw new InvalidOperationException("Text model is not loaded.");
        }

        var @params = parameters ?? GetDefaultParams();
        _handle = MtmdNative.mtmd_init_from_file(projectorPath, textModel.ModelHandle, @params);

        if (_handle != IntPtr.Zero)
        {
            InitializeProperties(IntPtr.Zero);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tokenizes text and bitmaps into input chunks.
    /// </summary>
    /// <param name="text">Input text prompt.</param>
    /// <param name="bitmaps">Array of bitmap images/audio.</param>
    /// <returns>Input chunks collection, or null on failure.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public MtmdInputChunks? Tokenize(string text, MtmdBitmap[] bitmaps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        var inputText = new MtmdInputText
        {
            Text = Marshal.StringToHGlobalAnsi(text),
            TextLen = (nuint)text.Length,
            AddSpecial = true,
            ParseSpecial = true
        };

        IntPtr[] bitmapHandles = new IntPtr[bitmaps.Length];
        for (int i = 0; i < bitmaps.Length; i++)
        {
            bitmapHandles[i] = bitmaps[i].DangerousGetHandle();
        }

        IntPtr chunksHandle = MtmdNative.mtmd_input_chunks_init();

        try
        {
            int result = MtmdNative.mtmd_tokenize(_handle, chunksHandle, inputText, bitmapHandles, (nuint)bitmaps.Length);

            if (result == 0)
            {
                return new MtmdInputChunks(chunksHandle);
            }

            MtmdNative.mtmd_input_chunks_free(chunksHandle);
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(inputText.Text);
        }
    }

    /// <summary>
    /// Encodes a single input chunk.
    /// </summary>
    /// <param name="chunk">Chunk to encode.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public int EncodeChunk(MtmdInputChunk chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return MtmdNative.mtmd_encode_chunk(_handle, chunk.DangerousGetHandle());
    }

    /// <summary>
    /// Gets output embeddings from the last encode pass.
    /// </summary>
    /// <returns>Pointer to embeddings array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public IntPtr GetOutputEmbeddings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return MtmdNative.mtmd_get_output_embd(_handle);
    }

    /// <summary>
    /// Creates a batch for encoding multiple chunks.
    /// </summary>
    /// <returns>New batch instance.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public MtmdBatch CreateBatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        IntPtr batchHandle = MtmdNative.mtmd_batch_init(_handle);
        return new MtmdBatch(batchHandle);
    }

    /// <summary>
    /// Creates an image bitmap from raw RGB data.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="data">RGB pixel data (width * height * 3 bytes).</param>
    /// <returns>New bitmap instance.</returns>
    public static MtmdBitmap CreateImageBitmap(uint width, uint height, byte[] data)
    {
        if (data.Length != width * height * 3)
        {
            throw new ArgumentException("Data size must be width * height * 3 for RGB format.", nameof(data));
        }

        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, dataPtr, data.Length);

        IntPtr handle = MtmdNative.mtmd_bitmap_init(width, height, dataPtr);

        // The native library takes ownership of the data pointer
        // Don't free it here

        return new MtmdBitmap(handle);
    }

    /// <summary>
    /// Creates an audio bitmap from PCM float data.
    /// </summary>
    /// <param name="samples">Audio samples as float array.</param>
    /// <returns>New bitmap instance.</returns>
    public static MtmdBitmap CreateAudioBitmap(float[] samples)
    {
        IntPtr dataPtr = Marshal.AllocHGlobal(samples.Length * sizeof(float));
        Marshal.Copy(samples, 0, dataPtr, samples.Length);

        IntPtr handle = MtmdNative.mtmd_bitmap_init_from_audio((nuint)samples.Length, dataPtr);

        return new MtmdBitmap(handle);
    }

    /// <summary>
    /// Gets the default media marker.
    /// </summary>
    /// <returns>Default marker string.</returns>
    public static string GetDefaultMarker()
    {
        IntPtr ptr = MtmdNative.mtmd_default_marker();
        return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) ?? "<__media__>" : "<__media__>";
    }

    /// <summary>
    /// Gets default context parameters.
    /// </summary>
    /// <returns>Default parameters structure.</returns>
    public static MtmdContextParams GetDefaultParams()
    {
        return MtmdNative.mtmd_context_params_default();
    }

    private void InitializeProperties(IntPtr /*chunk*/ _)
    {
        SupportsVision = MtmdNative.mtmd_support_vision(_handle);
        SupportsAudio = MtmdNative.mtmd_support_audio(_handle);
        AudioSampleRate = MtmdNative.mtmd_get_audio_sample_rate(_handle);

        IntPtr markerPtr = MtmdNative.mtmd_get_marker(_handle);
        MediaMarker = markerPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(markerPtr) : null;

        // mRoPE and non-causal mask depend on a per-chunk native API call,
        // so conservative defaults are set here and can be overridden per chunk
        UsesMRope = false;
        UsesNonCausalMask = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            MtmdNative.mtmd_free(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
