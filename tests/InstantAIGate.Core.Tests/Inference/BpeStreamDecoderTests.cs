namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Native.Inference;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

public sealed class BpeStreamDecoderTests
{
    private static List<string> CollectAll(Action<BpeStreamDecoder, List<string>> driver)
    {
        var chunks = new List<string>();
        var decoder = new BpeStreamDecoder();
        driver(decoder, chunks);
        return chunks;
    }

    [Fact]
    public void SingleByteAscii_YieldsImmediatelyWithoutPending()
    {
        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();

        // Feed enough ASCII bytes to cross YieldFlushThreshold (8)
        byte[] ascii = "Hello, World!"u8.ToArray();
        foreach (byte b in ascii)
        {
            decoder.Feed(new[] { b }, 1, chunks.Add);
        }
        decoder.Flush(chunks.Add);

        Assert.Equal("Hello, World!", string.Concat(chunks));
        Assert.Equal(0, decoder.PendingByteCount);
        // Streaming guarantee: crossed flush threshold, produces multiple chunks before and at Flush()
        Assert.True(chunks.Count > 1, "ASCII input should produce multiple yield chunks for real-time streaming.");
    }

    [Fact]
    public void MultibyteUtf8_SplitAtEveryByteBoundary_ReassemblesPerfectly()
    {
        // Worst case for BPE: feed each UTF-8 byte as a separate "token".
        // Multibyte characters (e.g. accented letters, umlauts: 2 bytes each).
        // This is exactly the scenario that split-token BPE exercises.
        string original = "Café & Naïve résumé, greeting: Hello World!";
        byte[] utf8 = Encoding.UTF8.GetBytes(original);

        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();

        for (int i = 0; i < utf8.Length; i++)
        {
            decoder.Feed(utf8.AsSpan(i, 1).ToArray(), 1, chunks.Add);
        }
        decoder.Flush(chunks.Add);

        Assert.Equal(original, string.Concat(chunks));
        Assert.Equal(0, decoder.PendingByteCount);
    }

    [Fact]
    public void FourByteEmoji_SplitAcrossThreePieces_PreservesSurrogatePair()
    {
        // Rocket emoji 🚀 is U+1F680, encoded as 4 UTF-8 bytes: F0 9F 9A 80
        // In UTF-16 it becomes a surrogate pair (2 chars). This test verifies:
        //   1. 4-byte sequences survive BPE splitting.
        //   2. Surrogate pairs are never broken mid-pair (no lone surrogates).
        //   3. Yield buffer reserves room for surrogates before encoding.
        string original = "Hello 🚀 World 🎉!";
        byte[] utf8 = Encoding.UTF8.GetBytes(original);

        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();

        // Split at every byte — maximum torture for the decoder
        foreach (byte b in utf8)
        {
            decoder.Feed(new[] { b }, 1, chunks.Add);
        }
        decoder.Flush(chunks.Add);

        string reassembled = string.Concat(chunks);
        Assert.Equal(original, reassembled);
        Assert.Equal(0, decoder.PendingByteCount);

        // Integrity check: no orphan surrogates anywhere in the output
        for (int i = 0; i < reassembled.Length; i++)
        {
            char c = reassembled[i];
            if (char.IsHighSurrogate(c))
            {
                Assert.True(i + 1 < reassembled.Length && char.IsLowSurrogate(reassembled[i + 1]),
                    $"Orphan high surrogate at position {i}");
            }
            else if (char.IsLowSurrogate(c))
            {
                Assert.True(i > 0 && char.IsHighSurrogate(reassembled[i - 1]),
                    $"Orphan low surrogate at position {i}");
            }
        }
    }

    [Fact]
    public void LongText_RandomSplitBoundaries_NoDataLoss()
    {
        // Stress test: 1000 mixed characters, split at pseudo-random byte boundaries.
        // Simulates real-world BPE output where token piece lengths vary 1-16 bytes.
        var rng = new Random(42);
        string[] emojis = ["🚀", "🎉", "🔥", "💯", "🌟"];
        string latinExtended = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZéàèùâêîôûëïüçñ";
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            // Mix of ASCII, Latin-extended multibyte chars, and emoji
            int roll = rng.Next(3);
            if (roll == 0) sb.Append((char)rng.Next('a', 'z' + 1));
            else if (roll == 1) sb.Append(latinExtended[rng.Next(latinExtended.Length)]);
            else sb.Append(emojis[rng.Next(emojis.Length)]);
        }
        string original = sb.ToString();
        byte[] utf8 = Encoding.UTF8.GetBytes(original);

        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();
        int offset = 0;

        while (offset < utf8.Length)
        {
            int pieceLen = Math.Min(rng.Next(1, 17), utf8.Length - offset);
            byte[] piece = new byte[pieceLen];
            Buffer.BlockCopy(utf8, offset, piece, 0, pieceLen);
            decoder.Feed(piece, pieceLen, chunks.Add);
            offset += pieceLen;
        }
        decoder.Flush(chunks.Add);

        Assert.Equal(original, string.Concat(chunks));
        Assert.Equal(0, decoder.PendingByteCount);
        Assert.Equal(utf8.LongLength, decoder.TotalFedBytes);
    }

    [Fact]
    public void IncompleteTrailingBytes_RemainPendingAfterFlush()
    {
        // Feed only the leading byte of a 2-byte UTF-8 sequence (e.g. 'é' is 0xC3 0xA9) and stop.
        // Decoder must NOT throw, NOT emit garbage, and MUST report pending bytes
        // so the caller can log a tokenizer warning.
        byte[] incomplete = { 0xC3 }; // leading byte of 'é' (0xC3 0xA9)

        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();
        decoder.Feed(incomplete, 1, chunks.Add);
        decoder.Flush(chunks.Add);

        Assert.Empty(chunks);
        Assert.Equal(1, decoder.PendingByteCount);
    }

    [Fact]
    public void MalformedBytes_AreSkippedWithoutStallingStream()
    {
        // 0xFF is never valid in UTF-8. Decoder should skip it and continue
        // decoding surrounding valid bytes rather than stalling or throwing.
        // 0xC3 0xA9 is 'é', 0xC3 0xBC is 'ü'
        byte[] payload = { 0xFF, 0xC3, 0xA9, 0x80, 0xC3, 0xBC }; // [bad] é [bad] ü

        var decoder = new BpeStreamDecoder();
        var chunks = new List<string>();
        decoder.Feed(payload, payload.Length, chunks.Add);
        decoder.Flush(chunks.Add);

        string result = string.Concat(chunks);
        Assert.Contains("é", result);
        Assert.Contains("ü", result);
        Assert.Equal(0, decoder.PendingByteCount);
    }

    [Fact]
    public void BufferAliasing_PieceArrayIsNotPendingArray()
    {
        // Regression test for the exact bug that caused 5 re-fixes:
        // if llama_token_to_piece's output buffer was aliased with the decoder's
        // internal pending buffer, the next token would overwrite the undecoded
        // tail of the previous token, silently destroying multibyte characters.
        //
        // We verify the contract: passing the SAME byte[] as piece multiple times
        // in succession must produce the same output as passing distinct arrays.
        string original = "Hello Café 🚀";
        byte[] utf8 = Encoding.UTF8.GetBytes(original);

        // Scenario A: distinct arrays per call (normal usage)
        var decoderA = new BpeStreamDecoder();
        var chunksA = new List<string>();
        foreach (byte b in utf8)
        {
            decoderA.Feed(new[] { b }, 1, chunksA.Add);
        }
        decoderA.Flush(chunksA.Add);

        // Scenario B: reuse the same array reference, mutating it each time
        // (what would happen if llama.cpp and decoder shared a buffer)
        var decoderB = new BpeStreamDecoder();
        var chunksB = new List<string>();
        byte[] reused = new byte[1];
        foreach (byte b in utf8)
        {
            reused[0] = b;
            decoderB.Feed(reused, 1, chunksB.Add);
        }
        decoderB.Flush(chunksB.Add);

        // Both scenarios must produce identical, correct output.
        // If decoder internally aliased the piece buffer, scenario B would corrupt.
        Assert.Equal(string.Concat(chunksA), string.Concat(chunksB));
        Assert.Equal(original, string.Concat(chunksA));
        Assert.Equal(0, decoderA.PendingByteCount);
        Assert.Equal(0, decoderB.PendingByteCount);
    }

    [Fact]
    public void Performance_100kChars_DoesNotRegressQuadratically()
    {
        // Guard against accidental O(n²) behavior in the pending-buffer shift.
        // 100k chars of 2-byte UTF-8 (~200k UTF-8 bytes) fed one byte at a time must
        // complete in well under a second on modern hardware.
        string original = new string('é', 100_000);
        byte[] utf8 = Encoding.UTF8.GetBytes(original);

        var decoder = new BpeStreamDecoder();
        int chunkCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < utf8.Length; i++)
        {
            decoder.Feed(utf8.AsSpan(i, 1).ToArray(), 1, _ => chunkCount++);
        }
        decoder.Flush(_ => chunkCount++);

        sw.Stop();

        Assert.Equal(original, original); // sanity
        Assert.Equal(0, decoder.PendingByteCount);
        Assert.True(chunkCount > 0);
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Decoder took {sw.ElapsedMilliseconds}ms for 100k chars — possible O(n²) regression.");
    }
}
