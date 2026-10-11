namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

public sealed class GatewayStateManagerTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly GatewayStateManager _stateManager;

    public GatewayStateManagerTests()
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        _stateManager = new GatewayStateManager(_timeProvider);
    }

    [Fact]
    public void InitialState_ShouldBeUninitialized()
    {
        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.Uninitialized, snapshot.Status);
        Assert.Null(snapshot.ActiveModelId);
        Assert.Equal(0.0f, snapshot.ProgressPercentage);
        Assert.Equal(_timeProvider.GetUtcNow(), snapshot.Timestamp);
    }

    [Fact]
    public void SetDownloading_ShouldUpdateStateAndPublishEvent()
    {
        GatewayStatusDetails? receivedEvent = null;
        _stateManager.StatusChanged += status => receivedEvent = status;

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _stateManager.SetDownloading(
            modelId: "test-model-8b",
            progressPercentage: 45.5f,
            downloadedBytes: 455_000_000,
            totalBytes: 1_000_000_000,
            bytesPerSecond: 25_000_000,
            stageDescription: "Downloading chunk 2/4");

        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.ModelDownloading, snapshot.Status);
        Assert.Equal("test-model-8b", snapshot.ActiveModelId);
        Assert.Equal(45.5f, snapshot.ProgressPercentage);
        Assert.Equal(455_000_000, snapshot.DownloadedBytes);
        Assert.Equal(1_000_000_000, snapshot.TotalBytes);
        Assert.Equal(25_000_000, snapshot.BytesPerSecond);
        Assert.Equal("Downloading chunk 2/4", snapshot.StageDescription);
        Assert.Equal(_timeProvider.GetUtcNow(), snapshot.Timestamp);
        Assert.NotNull(receivedEvent);
        Assert.Equal(snapshot, receivedEvent);
    }

    [Fact]
    public void SetLoading_ShouldSetProgress100AndStateModelLoading()
    {
        _stateManager.SetLoading("test-model-8b", "Mapping GGUF tensors to VRAM");

        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.ModelLoading, snapshot.Status);
        Assert.Equal("test-model-8b", snapshot.ActiveModelId);
        Assert.Equal(100.0f, snapshot.ProgressPercentage);
        Assert.Equal("Mapping GGUF tensors to VRAM", snapshot.StageDescription);
    }

    [Fact]
    public void SetReady_ShouldTransitionToOperationalReadiness()
    {
        _stateManager.SetReady("test-model-8b");

        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.Ready, snapshot.Status);
        Assert.Equal("test-model-8b", snapshot.ActiveModelId);
        Assert.Equal(100.0f, snapshot.ProgressPercentage);
        Assert.Null(snapshot.ErrorMessage);
    }

    [Fact]
    public void SetFaulted_ShouldCaptureErrorMessageAndUnderlyingException()
    {
        var innerException = new InvalidOperationException("CUDA out of memory");
        _stateManager.SetFaulted("test-model-8b", "Failed to allocate KV cache", innerException);

        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.Faulted, snapshot.Status);
        Assert.Equal("test-model-8b", snapshot.ActiveModelId);
        Assert.Equal("Failed to allocate KV cache: CUDA out of memory", snapshot.ErrorMessage);
    }

    [Fact]
    public void Reset_ShouldRevertToUninitialized()
    {
        _stateManager.SetReady("test-model-8b");
        _stateManager.Reset();

        var snapshot = _stateManager.GetSnapshot();

        Assert.Equal(GatewayOperationalStatus.Uninitialized, snapshot.Status);
        Assert.Null(snapshot.ActiveModelId);
        Assert.Equal(0.0f, snapshot.ProgressPercentage);
    }

    [Fact]
    public void EventObserverException_DoesNotCorruptStateOrBlockOtherSubscribers()
    {
        bool healthySubscriberCalled = false;
        _stateManager.StatusChanged += _ => throw new InvalidOperationException("Faulty observer");
        _stateManager.StatusChanged += _ => healthySubscriberCalled = true;

        var exception = Record.Exception(() => _stateManager.SetReady("test-model-8b"));

        Assert.Null(exception);
        Assert.True(healthySubscriberCalled);
        Assert.Equal(GatewayOperationalStatus.Ready, _stateManager.GetSnapshot().Status);
    }

    [Fact]
    public async Task ConcurrentStateTransitions_ShouldRemainThreadSafe()
    {
        const int iterations = 1000;
        var tasks = new List<Task>();

        for (int i = 0; i < 16; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    if (j % 2 == 0)
                    {
                        _stateManager.SetDownloading(
                            $"model-{threadId}",
                            (float)(j % 100),
                            j * 1024,
                            100 * 1024,
                            5000);
                    }
                    else
                    {
                        _stateManager.GetSnapshot();
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        var finalSnapshot = _stateManager.GetSnapshot();
        Assert.NotNull(finalSnapshot);
        Assert.True(finalSnapshot.DownloadedBytes > 0);
    }
}