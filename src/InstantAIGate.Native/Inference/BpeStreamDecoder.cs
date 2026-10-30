namespace InstantAIGate.Native.Inference;

using System;
using System.Buffers;
using System.Text;

/// <summary>
/// Stateful UTF-8 streaming decoder designed for BPE tokenizers (Qwen3, Llama 3, Mistral).
///
/// BPE tokenizers routinely split multibyte UTF-8 codepoints across multiple tokens.
/// For example, the Cyrillic letter "ё" (2 bytes: 0xD1 0x91) may arrive as two
/// separate tokens: one carrying 0xD1, the next carrying 0x91. A naive decoder
/// that calls GetChars(flush: false) per token and yields only complete results
/// will silently drop the leading byte when the second half never arrives — or
/// stall the stream waiting for bytes that only come in the next token.
///
/// This decoder:
///   1. Accumulates raw bytes in a private pending buffer.
///   2. Uses Rune.DecodeFromUtf8 to consume as many complete codepoints as possible.
///   3. Preserves the undecoded tail (OperationStatus.NeedMoreData) for the next Feed call.
///   4. Yields decoded chars via a small yieldBuffer, flushing every ~8 chars for
///      real-time streaming rather than batching per-token.
///   5. Guarantees no aliasing between input piece buffer and internal pending buffer,
///      eliminating the silent byte-loss bug that destroyed multibyte characters.
///
/// Thread safety: NOT thread-safe. One instance per streaming session.
/// </summary>
internal sealed class BpeStreamDecoder
{
    private const int InitialPendingCapacity = 64;
    private const int YieldBufferCapacity = 16;
    private const int YieldFlushThreshold = 8;

    private byte[] _pendingBytes;
    private int _pendingCount;
    private readonly char[] _yieldBuffer;
    private int _yieldBufferPos;
    private long _totalFedBytes;
    private long _totalYieldedChars;

    public BpeStreamDecoder()
    {
        _pendingBytes = new byte[InitialPendingCapacity];
        _pendingCount = 0;
        _yieldBuffer = new char[YieldBufferCapacity];
        _yieldBufferPos = 0;
    }

    public int PendingByteCount => _pendingCount;
    public long TotalFedBytes => _totalFedBytes;
    public long TotalYieldedChars => _totalYieldedChars;

    /// <summary>
    /// Feeds a new token piece (raw UTF-8 bytes) into the decoder.
    /// Any complete UTF-8 codepoints are decoded and emitted via <paramref name="onChunk"/>.
    /// Incomplete trailing bytes are retained for the next call.
    /// </summary>
    /// <param name="piece">Buffer containing the token piece bytes.</param>
    /// <param name="length">Number of valid bytes in <paramref name="piece"/>.</param>
    /// <param name="onChunk">
    /// Callback invoked zero or more times with decoded string chunks.
    /// The callback must not throw; exceptions will propagate and corrupt state.
    /// </param>
    public void Feed(byte[] piece, int length, Action<string> onChunk)
    {
        if (piece is null) throw new ArgumentNullException(nameof(piece));
        if (length < 0 || length > piece.Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (onChunk is null) throw new ArgumentNullException(nameof(onChunk));
        if (length == 0) return;

        _totalFedBytes += length;

        // Append new piece AFTER the undecoded tail. No aliasing — piece and
        // _pendingBytes are guaranteed to be separate arrays by the caller
        // (LlamaInference keeps tokenPieceBuffer distinct from decoder state).
        if (_pendingCount + length > _pendingBytes.Length)
        {
            int newSize = Math.Max(_pendingBytes.Length * 2, _pendingCount + length);
            Array.Resize(ref _pendingBytes, newSize);
        }
        Buffer.BlockCopy(piece, 0, _pendingBytes, _pendingCount, length);
        _pendingCount += length;

        DrainCompleteCodepoints(onChunk);
        FlushYieldBufferIfNeeded(onChunk);
    }

    /// <summary>
    /// Convenience overload accepting ReadOnlySpan&lt;byte&gt;. Copies into a temp
    /// array since the decoder must retain bytes across calls.
    /// </summary>
    public void Feed(ReadOnlySpan<byte> piece, Action<string> onChunk)
    {
        if (piece.IsEmpty) return;
        byte[] temp = piece.ToArray();
        Feed(temp, temp.Length, onChunk);
    }

    /// <summary>
    /// Flushes any buffered chars in the yield buffer. Call this once at the end
    /// of the stream. Does NOT recover incomplete trailing UTF-8 sequences — those
    /// remain in PendingByteCount and indicate a tokenizer bug in the source model.
    /// </summary>
    public void Flush(Action<string> onChunk)
    {
        if (onChunk is null) throw new ArgumentNullException(nameof(onChunk));
        if (_yieldBufferPos > 0)
        {
            onChunk(new string(_yieldBuffer, 0, _yieldBufferPos));
            _totalYieldedChars += _yieldBufferPos;
            _yieldBufferPos = 0;
        }
    }

    private void DrainCompleteCodepoints(Action<string> onChunk)
    {
        int decoded = 0;
        while (decoded < _pendingCount)
        {
            OperationStatus status = Rune.DecodeFromUtf8(
                _pendingBytes.AsSpan(decoded, _pendingCount - decoded),
                out Rune rune,
                out int bytesConsumed);

            if (status == OperationStatus.Done)
            {
                // Guarantee room for a surrogate pair BEFORE encoding.
                if (_yieldBufferPos + 2 > _yieldBuffer.Length)
                {
                    onChunk(new string(_yieldBuffer, 0, _yieldBufferPos));
                    _totalYieldedChars += _yieldBufferPos;
                    _yieldBufferPos = 0;
                }
                _yieldBufferPos += rune.EncodeToUtf16(_yieldBuffer.AsSpan(_yieldBufferPos));
                decoded += bytesConsumed;
            }
            else if (status == OperationStatus.NeedMoreData)
            {
                break; // split codepoint — wait for the next Feed call
            }
            else // InvalidData
            {
                decoded += Math.Max(1, bytesConsumed); // skip malformed byte
            }
        }

        if (decoded > 0)
        {
            if (decoded < _pendingCount)
            {
                Buffer.BlockCopy(_pendingBytes, decoded, _pendingBytes, 0, _pendingCount - decoded);
            }
            _pendingCount -= decoded;
        }
    }

    private void FlushYieldBufferIfNeeded(Action<string> onChunk)
    {
        if (_yieldBufferPos >= YieldFlushThreshold)
        {
            onChunk(new string(_yieldBuffer, 0, _yieldBufferPos));
            _totalYieldedChars += _yieldBufferPos;
            _yieldBufferPos = 0;
        }
    }
}