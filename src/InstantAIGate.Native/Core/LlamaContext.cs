// SPDX-FileCopyrightText: (c) InstantAI Gate Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using InstantAIGate.Native.Bindings;

namespace InstantAIGate.Native.Core;

/// <summary>
/// High-level wrapper for llama context management.
/// </summary>
public sealed class LlamaContext : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;
    private readonly LlamaModel _model;

    /// <summary>
    /// Gets a value indicating whether the context is initialized.
    /// </summary>
    public bool IsInitialized => _handle != IntPtr.Zero && !_disposed;

    /// <summary>
    /// Gets the context size.
    /// </summary>
    public uint ContextSize { get; private set; }

    /// <summary>
    /// Gets the maximum sequence count.
    /// </summary>
    public uint MaxSequenceCount { get; private set; }

    /// <summary>
    /// Gets the batch size.
    /// </summary>
    public uint BatchSize { get; private set; }

    /// <summary>
    /// Gets the physical batch size.
    /// </summary>
    public uint PhysicalBatchSize { get; private set; }

    /// <summary>
    /// Gets the pooling type.
    /// </summary>
    public LlamaPoolingType PoolingType { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaContext"/> class.
    /// </summary>
    /// <param name="model">The model to create context for.</param>
    public LlamaContext(LlamaModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _handle = IntPtr.Zero;
    }

    /// <summary>
    /// Initializes the context with the specified parameters.
    /// </summary>
    /// <param name="parameters">Context parameters.</param>
    /// <returns><see langword="true"/> if initialization was successful.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model is not loaded.</exception>
    public bool Initialize(LlamaContextParams? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_model.IsLoaded)
        {
            throw new InvalidOperationException("Model is not loaded.");
        }

        var @params = parameters ?? LlamaNative.llama_context_default_params();
        
        // Get model handle through reflection or store it in LlamaModel
        // For now, we assume the model has a way to expose its handle
        _handle = LlamaNative.llama_init_from_model(GetModelHandle(), @params);

        if (_handle != IntPtr.Zero)
        {
            InitializeProperties();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Encodes a batch of tokens.
    /// </summary>
    /// <param name="batch">Batch to encode.</param>
    /// <returns>0 on success, negative value on error.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public int Encode(in LlamaBatch batch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_encode(_handle, batch);
    }

    /// <summary>
    /// Decodes a batch of tokens.
    /// </summary>
    /// <param name="batch">Batch to decode.</param>
    /// <returns>0 on success, negative value on error.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public int Decode(in LlamaBatch batch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_decode(_handle, batch);
    }

    /// <summary>
    /// Gets logits for all tokens.
    /// </summary>
    /// <returns>Pointer to logits array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public IntPtr GetLogits()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_get_logits(_handle);
    }

    /// <summary>
    /// Gets logits for a specific token position.
    /// </summary>
    /// <param name="i">Token index.</param>
    /// <returns>Pointer to logits for the token.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public IntPtr GetLogitsIth(int i)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_get_logits_ith(_handle, i);
    }

    /// <summary>
    /// Gets embeddings for all sequences.
    /// </summary>
    /// <returns>Pointer to embeddings array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public IntPtr GetEmbeddings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_get_embeddings(_handle);
    }

    /// <summary>
    /// Gets performance metrics for the context.
    /// </summary>
    /// <returns>Performance data structure.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public LlamaPerfContextData GetPerfData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        return LlamaNative.llama_perf_context(_handle);
    }

    /// <summary>
    /// Prints performance metrics to standard output.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public void PrintPerf()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        LlamaNative.llama_perf_context_print(_handle);
    }

    /// <summary>
    /// Resets performance metrics.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the context is disposed.</exception>
    public void ResetPerf()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Context is not initialized.");
        }

        LlamaNative.llama_perf_context_reset(_handle);
    }

    /// <summary>
    /// Creates a batch structure for token input.
    /// </summary>
    /// <param name="nTokens">Number of tokens.</param>
    /// <param name="nSeqMax">Maximum number of sequences.</param>
    /// <param name="useEmbd">Whether to use embeddings instead of tokens.</param>
    /// <returns>Initialized batch structure.</returns>
    public static LlamaBatch CreateBatch(int nTokens, int nSeqMax, bool useEmbd = false)
    {
        int tokenSize = sizeof(int);
        int posSize = sizeof(int);
        int seqIdSize = sizeof(int);
        int logitsSize = sizeof(byte);

        int totalTokens = nTokens;
        int totalSeqIds = nSeqMax > 0 ? nSeqMax : 1;

        IntPtr tokens = useEmbd ? IntPtr.Zero : Marshal.AllocHGlobal(totalTokens * tokenSize);
        IntPtr embd = useEmbd ? Marshal.AllocHGlobal(totalTokens * 4096 * sizeof(float)) : IntPtr.Zero;
        IntPtr pos = Marshal.AllocHGlobal(totalTokens * posSize);
        IntPtr nSeqId = Marshal.AllocHGlobal(totalTokens * seqIdSize);
        IntPtr seqId = Marshal.AllocHGlobal(totalTokens * IntPtr.Size);
        IntPtr logits = Marshal.AllocHGlobal(totalTokens * logitsSize);

        // Initialize n_seq_id to 1 for all tokens
        for (int i = 0; i < totalTokens; i++)
        {
            Marshal.WriteInt32(nSeqId, i * seqIdSize, 1);
            Marshal.WriteByte(logits, i * logitsSize, 1);
        }

        // Initialize first seq_id array
        IntPtr firstSeqId = Marshal.AllocHGlobal(seqIdSize);
        Marshal.WriteInt32(firstSeqId, 0);
        Marshal.WriteIntPtr(seqId, firstSeqId);

        return new LlamaBatch
        {
            NTokens = totalTokens,
            Token = tokens,
            Embd = embd,
            Pos = pos,
            NSeqId = nSeqId,
            SeqId = seqId,
            Logits = logits
        };
    }

    /// <summary>
    /// Frees resources allocated for a batch.
    /// </summary>
    /// <param name="batch">Batch to free.</param>
    public static void FreeBatch(ref LlamaBatch batch)
    {
        if (batch.Token != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(batch.Token);
            batch.Token = IntPtr.Zero;
        }

        if (batch.Embd != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(batch.Embd);
            batch.Embd = IntPtr.Zero;
        }

        if (batch.Pos != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(batch.Pos);
            batch.Pos = IntPtr.Zero;
        }

        if (batch.NSeqId != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(batch.NSeqId);
            batch.NSeqId = IntPtr.Zero;
        }

        if (batch.SeqId != IntPtr.Zero)
        {
            // Free individual seq_id arrays
            //int seqIdSize = sizeof(int);
            for (int i = 0; i < batch.NTokens; i++)
            {
                IntPtr seqIdPtr = Marshal.ReadIntPtr(batch.SeqId, i * IntPtr.Size);
                if (seqIdPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(seqIdPtr);
                }
            }

            Marshal.FreeHGlobal(batch.SeqId);
            batch.SeqId = IntPtr.Zero;
        }

        if (batch.Logits != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(batch.Logits);
            batch.Logits = IntPtr.Zero;
        }

        batch.NTokens = 0;
    }

    private IntPtr GetModelHandle()
    {
        return _model.ModelHandle;
    }

    private void InitializeProperties()
    {
        ContextSize = LlamaNative.llama_n_ctx(_handle);
        MaxSequenceCount = LlamaNative.llama_n_ctx_seq(_handle);
        BatchSize = LlamaNative.llama_n_batch(_handle);
        PhysicalBatchSize = LlamaNative.llama_n_ubatch(_handle);
        PoolingType = LlamaNative.llama_pooling_type(_handle);
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
            LlamaNative.llama_free(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
