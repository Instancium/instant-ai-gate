// SPDX-FileCopyrightText: (c) InstantAI Gate Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using InstantAIGate.Native.Bindings;

namespace InstantAIGate.Native.Core;

/// <summary>
/// High-level wrapper for llama model management.
/// </summary>
public sealed class LlamaModel : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the model is loaded.
    /// </summary>
    public bool IsLoaded => _handle != IntPtr.Zero && !_disposed;

    /// <summary>
    /// Gets the model vocabulary handle.
    /// </summary>
    public IntPtr VocabHandle { get; private set; }

    /// <summary>
    /// Gets the training context size.
    /// </summary>
    public int ContextSizeTrain { get; private set; }

    /// <summary>
    /// Gets the embedding size.
    /// </summary>
    public int EmbeddingSize { get; private set; }

    /// <summary>
    /// Gets the input embedding size.
    /// </summary>
    public int EmbeddingSizeIn { get; private set; }

    /// <summary>
    /// Gets the output embedding size.
    /// </summary>
    public int EmbeddingSizeOut { get; private set; }

    /// <summary>
    /// Gets the number of layers.
    /// </summary>
    public int LayerCount { get; private set; }

    /// <summary>
    /// Gets the number of attention heads.
    /// </summary>
    public int HeadCount { get; private set; }

    /// <summary>
    /// Gets the number of key/value heads.
    /// </summary>
    public int HeadCountKv { get; private set; }

    /// <summary>
    /// Gets the RoPE frequency scale.
    /// </summary>
    public float RopeFreqScaleTrain { get; private set; }

    /// <summary>
    /// Gets the model file type.
    /// </summary>
    public LlamaFType FileType { get; private set; }

    /// <summary>
    /// Gets the model size in bytes.
    /// </summary>
    public ulong SizeBytes { get; private set; }

    /// <summary>
    /// Gets the total number of parameters.
    /// </summary>
    public ulong ParameterCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the model has an encoder.
    /// </summary>
    public bool HasEncoder { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the model has a decoder.
    /// </summary>
    public bool HasDecoder { get; private set; }

    /// <summary>
    /// Gets the RoPE type.
    /// </summary>
    public LlamaRopeType RopeType { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaModel"/> class.
    /// </summary>
    public LlamaModel()
    {
        _handle = IntPtr.Zero;
    }

    /// <summary>
    /// Loads a model from the specified file path.
    /// </summary>
    /// <param name="path">Path to the model file.</param>
    /// <param name="parameters">Model loading parameters.</param>
    /// <returns><see langword="true"/> if the model was loaded successfully.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the model is disposed.</exception>
    public bool Load(string path, LlamaModelParams? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var @params = parameters ?? LlamaNative.llama_model_default_params();
        _handle = LlamaNative.llama_model_load_from_file(path, @params, (nuint)path.Length);

        if (_handle != IntPtr.Zero)
        {
            InitializeProperties();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the chat template for the specified name.
    /// </summary>
    /// <param name="name">Template name (null for default).</param>
    /// <returns>The chat template string, or null if not found.</returns>
    public string? GetChatTemplate(string? name = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr result = LlamaNative.llama_model_chat_template(_handle, name);
        return result != IntPtr.Zero ? Marshal.PtrToStringUTF8(result) : null;
    }

    /// <summary>
    /// Gets metadata value by key.
    /// </summary>
    /// <param name="key">Metadata key.</param>
    /// <returns>Metadata value as string, or null if not found.</returns>
    public string? GetMetadataValue(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        const int bufferSize = 4096;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int result = LlamaNative.llama_model_meta_val_str(_handle, key, buffer, (nuint)bufferSize);
            return result >= 0 ? Marshal.PtrToStringUTF8(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Gets the model description.
    /// </summary>
    /// <returns>Model description string.</returns>
    public string GetDescription()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int result = LlamaNative.llama_model_desc(_handle, buffer, (nuint)bufferSize);
            return result >= 0 ? Marshal.PtrToStringUTF8(buffer) ?? string.Empty : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Tokenizes the specified text.
    /// </summary>
    /// <param name="text">Text to tokenize.</param>
    /// <param name="addSpecial">Whether to add special tokens.</param>
    /// <param name="parseSpecial">Whether to parse special tokens.</param>
    /// <returns>Array of token IDs.</returns>
    public int[] Tokenize(string text, bool addSpecial = true, bool parseSpecial = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (VocabHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Vocabulary is not initialized.");
        }

        int maxLength = text.Length * 2 + 16;
        IntPtr buffer = Marshal.AllocHGlobal(maxLength * sizeof(int));

        try
        {
            int count = LlamaNative.llama_tokenize(VocabHandle, text, text.Length, buffer, maxLength, addSpecial, parseSpecial);

            if (count < 0)
            {
                throw new InvalidOperationException($"Tokenization failed with error code: {count}");
            }

            int[] tokens = new int[count];
            Marshal.Copy(buffer, tokens, 0, count);
            return tokens;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Converts a token to its text representation.
    /// </summary>
    /// <param name="token">Token ID.</param>
    /// <param name="special">Whether to include special tokens.</param>
    /// <returns>Token text.</returns>
    public string TokenToPiece(int token, bool special = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (VocabHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Vocabulary is not initialized.");
        }

        const int bufferSize = 256;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int length = LlamaNative.llama_token_to_piece(VocabHandle, token, buffer, bufferSize, special);

            if (length < 0)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Detokenizes an array of tokens to text.
    /// </summary>
    /// <param name="tokens">Token IDs.</param>
    /// <param name="removeSpecial">Whether to remove special tokens.</param>
    /// <returns>Detokenized text.</returns>
    public string Detokenize(int[] tokens, bool removeSpecial = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (VocabHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Vocabulary is not initialized.");
        }

        int maxTextSize = tokens.Length * 32;
        IntPtr tokensPtr = Marshal.AllocHGlobal(tokens.Length * sizeof(int));
        IntPtr textBuffer = Marshal.AllocHGlobal(maxTextSize);

        try
        {
            Marshal.Copy(tokens, 0, tokensPtr, tokens.Length);

            int length = LlamaNative.llama_detokenize(
                VocabHandle,
                tokensPtr,
                tokens.Length,
                textBuffer,
                maxTextSize,
                removeSpecial);

            if (length < 0)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[length];
            Marshal.Copy(textBuffer, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(tokensPtr);
            Marshal.FreeHGlobal(textBuffer);
        }
    }

    /// <summary>
    /// Gets the internal model handle.
    /// </summary>
    internal IntPtr ModelHandle => _handle;

    private void InitializeProperties()
    {
        VocabHandle = LlamaNative.llama_model_get_vocab(_handle);
        ContextSizeTrain = LlamaNative.llama_model_n_ctx_train(_handle);
        EmbeddingSize = LlamaNative.llama_model_n_embd(_handle);
        EmbeddingSizeIn = LlamaNative.llama_model_n_embd_inp(_handle);
        EmbeddingSizeOut = LlamaNative.llama_model_n_embd_out(_handle);
        LayerCount = LlamaNative.llama_model_n_layer(_handle);
        HeadCount = LlamaNative.llama_model_n_head(_handle);
        HeadCountKv = LlamaNative.llama_model_n_head_kv(_handle);
        RopeFreqScaleTrain = LlamaNative.llama_model_rope_freq_scale_train(_handle);
        FileType = LlamaNative.llama_model_ftype(_handle);
        SizeBytes = LlamaNative.llama_model_size(_handle);
        ParameterCount = LlamaNative.llama_model_n_params(_handle);
        HasEncoder = LlamaNative.llama_model_has_encoder(_handle);
        HasDecoder = LlamaNative.llama_model_has_decoder(_handle);
        RopeType = LlamaNative.llama_model_rope_type(_handle);
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
            LlamaNative.llama_model_free(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
