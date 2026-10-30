namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Native.Inference;
using System.Collections.Generic;
using Xunit;

public sealed class RepetitionGuardTests
{
    [Fact]
    public void Feed_DistinctTokens_NeverTriggers()
    {
        var guard = new RepetitionGuard();
        for (int i = 0; i < 10_000; i++)
        {
            Assert.False(guard.Feed(i % 5000), $"Distinct stream must not trigger at token {i}");
        }
    }

    [Fact]
    public void Feed_PeriodicLoop_TriggersAfterThreshold()
    {
        // Simulate a degenerate loop: period-6 n-gram repeated forever.
        var guard = new RepetitionGuard();
        int[] period = { 10, 20, 30, 40, 50, 60 };
        bool triggered = false;
        int fedCount = 0;

        for (int repeat = 0; repeat < 100 && !triggered; repeat++)
        {
            foreach (int token in period)
            {
                fedCount++;
                triggered = guard.Feed(token);
                if (triggered) break;
            }
        }

        Assert.True(triggered, "Periodic repetition must be detected");
        // Window=24 tokens, max 6 repeats => trigger no later than ~7 full periods + window.
        Assert.True(fedCount < 24 + 60 * 8, $"Triggered too late: {fedCount} tokens");
    }

    [Fact]
    public void Feed_ShortBurstOfRepetition_DoesNotTrigger()
    {
        // Legitimate text can repeat a phrase 2-3 times (emphasis). Must not abort.
        var guard = new RepetitionGuard();
        int[] phrase = { 7, 7, 7, 7, 7, 7, 7, 7 };
        bool triggered = false;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            foreach (int t in phrase) triggered |= guard.Feed(t);
            // interleave distinct filler so windows differ between bursts
            for (int f = 100; f < 140; f++) triggered |= guard.Feed(f);
        }
        Assert.False(triggered, "Short legitimate repetition must not abort generation");
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var guard = new RepetitionGuard();
        for (int i = 0; i < 500; i++) guard.Feed(42); // force trigger
        guard.Reset();
        Assert.Equal(0, guard.TotalFed);
        Assert.False(guard.Feed(1));
        Assert.False(guard.Feed(2));
    }
}
