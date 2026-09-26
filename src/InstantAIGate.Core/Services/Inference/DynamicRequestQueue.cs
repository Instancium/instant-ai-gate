// src/InstantAIGate.Core/Services/Inference/DynamicRequestQueue.cs
namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Manages concurrency and backpressure for incoming inference requests.
/// Implements a lock-free fast-path for quota rejection and a dynamic capacity semaphore.
/// </summary>
public sealed class DynamicRequestQueue : IQueueManager, IDisposable
{
    private int _maxQueueSize;
    private int _pendingCount;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly TimeProvider _timeProvider;
    private volatile bool _isPaused;
    private readonly object _pauseLock = new();

    /// <summary>
    /// Gets the current maximum allowed concurrent requests in the queue.
    /// </summary>
    public int MaxQueueSize => Volatile.Read(ref _maxQueueSize);

    /// <summary>
    /// Gets the current number of requests waiting or executing.
    /// </summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>
    /// Initializes a new instance of the dynamic queue with a specified initial limit.
    /// </summary>
    /// <param name="initialLimit">The initial maximum capacity of the queue.</param>
    /// <param name="timeProvider">Optional time abstraction for deterministic testing.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when initial limit is zero or negative.</exception>
    public DynamicRequestQueue(int initialLimit, TimeProvider? timeProvider = null)
    {
        if (initialLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialLimit));

        _maxQueueSize = initialLimit;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _concurrencySemaphore = new SemaphoreSlim(initialLimit, int.MaxValue);
    }

    /// <summary>
    /// Dynamically adjusts the maximum capacity of the queue at runtime.
    /// Safely releases or drains semaphore permits based on the difference.
    /// </summary>
    /// <param name="newLimit">The new maximum capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the new limit is zero or negative.</exception>
    public void UpdateQueueLimit(int newLimit)
    {
        if (newLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(newLimit));

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
    }

    /// <summary>
    /// Suspends processing of new requests. Requests will wait until resumed or timed out.
    /// </summary>
    public void Pause() => _isPaused = true;

    /// <summary>
    /// Resumes processing of queued requests.
    /// </summary>
    public void Resume() => _isPaused = false;

    /// <summary>
    /// Attempts to acquire an execution lease from the queue. 
    /// Enforces strict backpressure limits and timeouts.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant making the request.</param>
    /// <param name="timeout">The maximum duration to wait in the queue.</param>
    /// <param name="ct">External cancellation token to abort the request.</param>
    /// <returns>An IDisposable lease that must be disposed to release the slot.</returns>
    /// <exception cref="QueueFullException">Thrown immediately if the queue is at maximum capacity.</exception>
    /// <exception cref="TimeoutException">Thrown if the execution slot cannot be acquired within the timeout.</exception>
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

            if (internalTimeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"Queue wait timed out after {timeout.TotalSeconds}s.");
            }

            throw;
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    /// <summary>
    /// Internal callback invoked by the QueueLease upon disposal to free the semaphore slot.
    /// </summary>
    internal void Release()
    {
        Interlocked.Decrement(ref _pendingCount);
        _concurrencySemaphore.Release();
    }

    /// <summary>
    /// Disposes the underlying semaphore resources.
    /// </summary>
    public void Dispose()
    {
        _concurrencySemaphore.Dispose();
    }
}