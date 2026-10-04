namespace InstantAIGate.Core.Tests.Inference;

using FluentAssertions;
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

        snapshot.Status.Should().Be(GatewayOperationalStatus.Uninitialized);
        snapshot.ActiveModelId.Should().BeNull();
        snapshot.ProgressPercentage.Should().Be(0.0f);
        snapshot.Timestamp.Should().Be(_timeProvider.GetUtcNow());
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

        snapshot.Status.Should().Be(GatewayOperationalStatus.ModelDownloading);
        snapshot.ActiveModelId.Should().Be("test-model-8b");
        snapshot.ProgressPercentage.Should().Be(45.5f);
        snapshot.DownloadedBytes.Should().Be(455_000_000);
        snapshot.TotalBytes.Should().Be(1_000_000_000);
        snapshot.BytesPerSecond.Should().Be(25_000_000);
        snapshot.StageDescription.Should().Be("Downloading chunk 2/4");
        snapshot.Timestamp.Should().Be(_timeProvider.GetUtcNow());

        receivedEvent.Should().NotBeNull();
        receivedEvent.Should().Be(snapshot);
    }

    [Fact]
    public void SetLoading_ShouldSetProgress100AndStateModelLoading()
    {
        _stateManager.SetLoading("test-model-8b", "Mapping GGUF tensors to VRAM");

        var snapshot = _stateManager.GetSnapshot();

        snapshot.Status.Should().Be(GatewayOperationalStatus.ModelLoading);
        snapshot.ActiveModelId.Should().Be("test-model-8b");
        snapshot.ProgressPercentage.Should().Be(100.0f);
        snapshot.StageDescription.Should().Be("Mapping GGUF tensors to VRAM");
    }

    [Fact]
    public void SetReady_ShouldTransitionToOperationalReadiness()
    {
        _stateManager.SetReady("test-model-8b");

        var snapshot = _stateManager.GetSnapshot();

        snapshot.Status.Should().Be(GatewayOperationalStatus.Ready);
        snapshot.ActiveModelId.Should().Be("test-model-8b");
        snapshot.ProgressPercentage.Should().Be(100.0f);
        snapshot.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void SetFaulted_ShouldCaptureErrorMessageAndUnderlyingException()
    {
        var innerException = new InvalidOperationException("CUDA out of memory");
        _stateManager.SetFaulted("test-model-8b", "Failed to allocate KV cache", innerException);

        var snapshot = _stateManager.GetSnapshot();

        snapshot.Status.Should().Be(GatewayOperationalStatus.Faulted);
        snapshot.ActiveModelId.Should().Be("test-model-8b");
        snapshot.ErrorMessage.Should().Be("Failed to allocate KV cache: CUDA out of memory");
    }

    [Fact]
    public void Reset_ShouldRevertToUninitialized()
    {
        _stateManager.SetReady("test-model-8b");
        _stateManager.Reset();

        var snapshot = _stateManager.GetSnapshot();

        snapshot.Status.Should().Be(GatewayOperationalStatus.Uninitialized);
        snapshot.ActiveModelId.Should().BeNull();
        snapshot.ProgressPercentage.Should().Be(0.0f);
    }

    [Fact]
    public void EventObserverException_DoesNotCorruptStateOrBlockOtherSubscribers()
    {
        bool healthySubscriberCalled = false;

        _stateManager.StatusChanged += _ => throw new InvalidOperationException("Faulty observer");
        _stateManager.StatusChanged += _ => healthySubscriberCalled = true;

        var act = () => _stateManager.SetReady("test-model-8b");
        act.Should().NotThrow();

        healthySubscriberCalled.Should().BeTrue();
        _stateManager.GetSnapshot().Status.Should().Be(GatewayOperationalStatus.Ready);
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
        finalSnapshot.Should().NotBeNull();
        finalSnapshot.DownloadedBytes.Should().BeGreaterThan(0);
    }
}