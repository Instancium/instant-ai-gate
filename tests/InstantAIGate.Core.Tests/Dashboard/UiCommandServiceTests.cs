namespace InstantAIGate.Core.Tests.Dashboard;

using InstantAIGate.Dashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class UiCommandServiceTests
{
    private static UiCommandService CreateService() => new(NullLogger<UiCommandService>.Instance);

    [Fact]
    public async Task RunAsync_WhenActionSucceeds_PublishesSucceededEventAndState()
    {
        var service = CreateService();
        UiCommandEvent? received = null;
        service.CommandCompleted += e => received = e;

        bool result = await service.RunAsync("cmd-1", "Test command", _ => Task.CompletedTask, "done.");

        Assert.True(result);
        Assert.NotNull(received);
        Assert.Equal(UiCommandStatus.Succeeded, received.Status);
        Assert.Equal("done.", received.Message);
        Assert.Equal(UiCommandStatus.Succeeded, service.GetState("cmd-1").Status);
        Assert.False(service.IsRunning("cmd-1"));
        Assert.Single(service.GetJournal());
    }

    [Fact]
    public async Task RunAsync_WhenActionThrows_SwallowsExceptionAndPublishesFailedEvent()
    {
        var service = CreateService();
        UiCommandEvent? received = null;
        service.CommandCompleted += e => received = e;

        bool result = await service.RunAsync("cmd-2", "Boom", _ => throw new InvalidOperationException("server said no"));

        Assert.False(result);
        Assert.NotNull(received);
        Assert.Equal(UiCommandStatus.Failed, received.Status);
        Assert.Equal("server said no", received.Message);
        Assert.Equal(UiCommandStatus.Failed, service.GetState("cmd-2").Status);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledExternally_ReportsFailedWithoutThrowing()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        bool result = await service.RunAsync(
            "cmd-3",
            "Cancellable",
            async ct =>
            {
                cts.Cancel();
                await Task.Delay(50, ct);
            },
            ct: cts.Token);

        Assert.False(result);
        Assert.Equal(UiCommandStatus.Failed, service.GetState("cmd-3").Status);
    }

    [Fact]
    public async Task RunAsync_ConcurrentDuplicateCommandId_IsRejected()
    {
        var service = CreateService();
        var gate = new SemaphoreSlim(0, 1);

        Task first = service.RunAsync("cmd-4", "Slow", async _ =>
        {
            gate.Release();
            await Task.Delay(200);
        });
        await gate.WaitAsync(TimeSpan.FromSeconds(2));

        bool second = await service.RunAsync("cmd-4", "Slow", _ => Task.CompletedTask);

        Assert.False(second);
        await first;
        Assert.Equal(UiCommandStatus.Succeeded, service.GetState("cmd-4").Status);
    }

    [Fact]
    public async Task Journal_IsCappedAtCapacity_AndEvictsOldestEntries()
    {
        var service = CreateService();
        int totalCommands = UiCommandService.JournalCapacity + 10;
        for (int i = 0; i < totalCommands; i++)
        {
            await service.RunAsync($"cmd-{i}", $"Op {i}", _ => Task.CompletedTask);
        }

        IReadOnlyList<OperationRecord> journal = service.GetJournal();
        Assert.Equal(UiCommandService.JournalCapacity, journal.Count);

        // Ring buffer eviction boundary: with 60 executions and capacity 50 the retained
        // window is commands [10..59]; anything older must be gone.
        Assert.Equal($"Op {totalCommands - UiCommandService.JournalCapacity}", journal[0].Label);
        Assert.Equal($"Op {totalCommands - 1}", journal[journal.Count - 1].Label);
        Assert.DoesNotContain(journal, r => r.Label == "Op 9");
    }
}