namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class DynamicRequestQueue : IQueueManager, IDisposable
{
    private int _maxQueueSize;
    private int _pendingCount;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly TimeProvider _timeProvider;
    private readonly IMetricsEventSource? _eventSource;
    private volatile bool _isPaused;
    private readonly object _pauseLock = new();

    public int MaxQueueSize => Volatile.Read(ref _maxQueueSize);
    public int PendingCount => Volatile.Read(ref _pendingCount);

    public DynamicRequestQueue(
        int initialLimit,
        TimeProvider? timeProvider = null,
        IMetricsEventSource? eventSource = null)
    {
        if (initialLimit <= 0) throw new ArgumentOutOfRangeException(nameof(initialLimit));
        _maxQueueSize = initialLimit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _eventSource = eventSource;
        _concurrencySemaphore = new SemaphoreSlim(initialLimit, int.MaxValue);
    }

    public void UpdateQueueLimit(int newLimit)
    {
        if (newLimit <= 0) throw new ArgumentOutOfRangeException(nameof(newLimit));
        lock (_pauseLock)
        {
            int oldLimit = _maxQueueSize;
            Volatile.Write(ref _maxQueueSize, newLimit);
            int diff = newLimit - oldLimit;
            if (diff > 0)
            {
                _concurrencySemaphore.Release(diff);
            }
            else if (diff < 0)
            {
                for (int i = 0; i < -diff; i++)
                {
                    _concurrencySemaphore.Wait(0);
                }
            }
        }
        _eventSource?.NotifyStateChanged();
    }

    public void Pause() => _isPaused = true;
    public void Resume() => _isPaused = false;

    public async Task<IQueueLease> EnqueueRequestAsync(string tenantId, TimeSpan timeout, CancellationToken ct = default)
    {
        while (true)
        {
            int currentCount = Volatile.Read(ref _pendingCount);
            int currentLimit = Volatile.Read(ref _maxQueueSize);
            if (currentCount >= currentLimit)
            {
                throw new QueueFullException(currentLimit);
            }
            if (Interlocked.CompareExchange(ref _pendingCount, currentCount + 1, currentCount) == currentCount)
            {
                _eventSource?.NotifyStateChanged();
                break;
            }
        }

        var startTimestamp = _timeProvider.GetTimestamp();
        using var internalTimeoutCts = new CancellationTokenSource(timeout, _timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalTimeoutCts.Token);
        try
        {
            while (_isPaused)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, linkedCts.Token);
            }

            bool acquired = await _concurrencySemaphore.WaitAsync(timeout, linkedCts.Token);
            if (!acquired)
            {
                throw new TimeoutException($"Failed to acquire execution slot within {timeout.TotalSeconds}s.");
            }

            var duration = _timeProvider.GetElapsedTime(startTimestamp);
            return new QueueLease(this, tenantId, duration);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _pendingCount);
            _eventSource?.NotifyStateChanged();
            if (internalTimeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"Queue wait timed out after {timeout.TotalSeconds}s.");
            }
            throw;
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            _eventSource?.NotifyStateChanged();
            throw;
        }
    }

    internal void Release()
    {
        Interlocked.Decrement(ref _pendingCount);
        _concurrencySemaphore.Release();
        _eventSource?.NotifyStateChanged();
    }

    public void Dispose()
    {
        _concurrencySemaphore.Dispose();
    }
}