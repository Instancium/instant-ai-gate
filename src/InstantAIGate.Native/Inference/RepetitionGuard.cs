namespace InstantAIGate.Native.Inference;

using System;
using System.Collections.Generic;

/// <summary>
/// Streaming degenerate-loop detector for autoregressive generation.
///
/// BPE samplers can fall into repetition collapse: the model emits the same
/// n-gram window indefinitely (e.g. "- **Situation:** ongoing, but ..." x20).
/// Without a hard cap this burns the entire context headroom and looks like
/// an infinite hang to the user.
///
/// The guard hashes fixed-size token n-gram windows and counts recurrences.
/// If the same window reappears more than <see cref="MaxWindowRepetitions"/>
/// times, generation is considered trapped in a loop and must be aborted.
///
/// Thread safety: NOT thread-safe. One instance per streaming session.
/// </summary>
internal sealed class RepetitionGuard
{
    private const int WindowSize = 24;          // tokens per hashed window
    private const int MaxWindowRepetitions = 6; // same window seen > N times => loop

    private readonly Queue<int> _window = new(WindowSize);
    private readonly Dictionary<ulong, int> _windowCounts = new();
    private int _totalFed;

    /// <summary>Total tokens inspected by the guard.</summary>
    public int TotalFed => _totalFed;

    /// <summary>
    /// Feeds one generated token. Returns true if a degenerate repetition
    /// loop has been detected and generation must stop immediately.
    /// </summary>
    public bool Feed(int tokenId)
    {
        _totalFed++;
        _window.Enqueue(tokenId);
        if (_window.Count < WindowSize)
        {
            return false;
        }
        if (_window.Count > WindowSize)
        {
            _window.Dequeue();
        }

        ulong hash = HashWindow();
        _windowCounts.TryGetValue(hash, out int count);
        count++;
        _windowCounts[hash] = count;
        return count > MaxWindowRepetitions;
    }

    /// <summary>Resets internal state for a new generation run.</summary>
    public void Reset()
    {
        _window.Clear();
        _windowCounts.Clear();
        _totalFed = 0;
    }

    private ulong HashWindow()
    {
        // FNV-1a over the token window: cheap, allocation-free, good dispersion.
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;
        ulong hash = FnvOffsetBasis;
        foreach (int token in _window)
        {
            hash ^= (uint)token;
            hash *= FnvPrime;
        }
        return hash;
    }
}
