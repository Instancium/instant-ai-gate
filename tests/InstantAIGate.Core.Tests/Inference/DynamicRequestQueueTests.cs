namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class DynamicRequestQueueTests
{
    [Fact]
    public async Task EnqueueRequestAsync_WithConcurrentRequests_StrictlyEnforcesQueueLimit()
    {
        var timeProvider = new FakeTimeProvider();
        var queue = new DynamicRequestQueue(50, timeProvider);
        using var cts = new CancellationTokenSource();

        var tasks = Enumerable.Range(0, 1000).Select(i =>
            queue.EnqueueRequestAsync($"tenant-{i}", TimeSpan.FromSeconds(5), cts.Token)
        ).ToList();

        var results = new List<IQueueLease>();
        var exceptions = new List<Exception>();

        foreach (var task in tasks)
        {
            try
            {
                results.Add(await task);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        Assert.Equal(50, results.Count);
        Assert.Equal(950, exceptions.Count(e => e is QueueFullException));
        Assert.Equal(50, queue.PendingCount);
    }

    [Fact]
    public async Task EnqueueRequestAsync_WhenPaused_ThrowsTimeoutExceptionAfterDeterministicWait()
    {
        var timeProvider = new FakeTimeProvider();
        using var queue = new DynamicRequestQueue(10, timeProvider);
        queue.Pause();

        var enqueueTask = queue.EnqueueRequestAsync("tenant-1", TimeSpan.FromSeconds(10));
        Assert.False(enqueueTask.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(11));
        await Assert.ThrowsAsync<TimeoutException>(() => enqueueTask);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task UpdateQueueLimit_ReducesCapacity_CorrectlyRejectsNewRequests()
    {
        var timeProvider = new FakeTimeProvider();
        using var queue = new DynamicRequestQueue(10, timeProvider);

        var leases = new List<IQueueLease>();
        for (int i = 0; i < 5; i++)
        {
            leases.Add(await queue.EnqueueRequestAsync($"tenant-{i}", TimeSpan.FromSeconds(1)));
        }
        Assert.Equal(5, queue.PendingCount);

        queue.UpdateQueueLimit(5);
        await Assert.ThrowsAsync<QueueFullException>(() =>
            queue.EnqueueRequestAsync("tenant-6", TimeSpan.FromSeconds(1)));

        leases[0].Dispose();
        Assert.Equal(4, queue.PendingCount);

        var newLease = await queue.EnqueueRequestAsync("tenant-7", TimeSpan.FromSeconds(1));
        Assert.NotNull(newLease);
        Assert.Equal(5, queue.PendingCount);
    }

    [Fact]
    public async Task EnqueueRequestAsync_NotifiesMetricsEventSourceOnStateChange()
    {
        var eventSource = new MetricsEventSource();
        using var queue = new DynamicRequestQueue(10, TimeProvider.System, eventSource);

        Assert.False(eventSource.Reader.TryRead(out _));

        var lease = await queue.EnqueueRequestAsync("tenant-1", TimeSpan.FromSeconds(1));
        Assert.True(eventSource.Reader.TryRead(out bool signaledOnEnqueue));
        Assert.True(signaledOnEnqueue);

        lease.Dispose();
        Assert.True(eventSource.Reader.TryRead(out bool signaledOnRelease));
        Assert.True(signaledOnRelease);
    }
}