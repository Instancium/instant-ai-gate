namespace InstantAIGate.Core.Services.Inference;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Manages request queue with backpressure and pause/resume capabilities.
/// </summary>
public sealed class RequestQueue
{
    private readonly SemaphoreSlim _queueSemaphore;
    private readonly int _maxQueueSize;
    private int _pendingCount;
    private bool _isPaused;
    private readonly object _pauseLock = new();

    /// <summary>
    /// Gets the number of pending requests in the queue.
    /// </summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>
    /// Initializes a new instance of the request queue.
    /// </summary>
    /// <param name="maxQueueSize">Maximum number of pending requests.</param>
    public RequestQueue(int maxQueueSize = 100)
    {
        _maxQueueSize = maxQueueSize;
        _queueSemaphore = new SemaphoreSlim(maxQueueSize, maxQueueSize);
        _pendingCount = 0;
        _isPaused = false;
    }

    /// <summary>
    /// Pauses the queue, preventing new requests from being processed.
    /// </summary>
    public void Pause()
    {
        lock (_pauseLock)
        {
            _isPaused = true;
        }
    }

    /// <summary>
    /// Resumes the queue, allowing new requests to be processed.
    /// </summary>
    public void Resume()
    {
        lock (_pauseLock)
        {
            _isPaused = false;
        }
    }

    /// <summary>
    /// Waits for a slot in the queue with backpressure support.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Task representing the wait operation.</returns>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_pauseLock)
            {
                if (!_isPaused) break;
            }

            await Task.Delay(50, ct);
        }

        await _queueSemaphore.WaitAsync(ct);
        Interlocked.Increment(ref _pendingCount);
    }

    /// <summary>
    /// Releases a slot in the queue after request completion.
    /// </summary>
    public void Release()
    {
        Interlocked.Decrement(ref _pendingCount);
        _queueSemaphore.Release();
    }

    /// <summary>
    /// Checks if the queue is currently paused.
    /// </summary>
    /// <returns>True if the queue is paused.</returns>
    public bool IsPaused()
    {
        lock (_pauseLock)
        {
            return _isPaused;
        }
    }
}