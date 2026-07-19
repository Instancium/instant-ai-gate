using InstantAIGate.Application.Dtos.Inference;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Bounded channel-based queue for managing pending inference requests with backpressure.
    /// </summary>
    public class RequestQueue
    {
        private readonly Channel<PendingInferenceRequest> _channel;
        private volatile TaskCompletionSource _pauseTcs;

        /// <summary>
        /// Gets the current number of pending requests in the queue.
        /// </summary>
        public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

        public RequestQueue()
        {
            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _channel = Channel.CreateBounded<PendingInferenceRequest>(options);
            _pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseTcs.SetResult();
        }

        /// <summary>
        /// Attempts to enqueue a new request. Returns false if the queue is full.
        /// </summary>
        public bool TryEnqueue(PendingInferenceRequest request)
        {
            return _channel.Writer.TryWrite(request);
        }

        /// <summary>
        /// Dequeues the next pending request, waiting asynchronously if the queue is empty or paused.
        /// </summary>
        public async ValueTask<PendingInferenceRequest> DequeueAsync(CancellationToken ct)
        {
            await _pauseTcs.Task.WaitAsync(ct);
            return await _channel.Reader.ReadAsync(ct);
        }

        /// <summary>
        /// Pauses the dequeue operation, holding consumers until resumed.
        /// </summary>
        public void Pause()
        {
            if (_pauseTcs.Task.IsCompleted)
            {
                _pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        /// <summary>
        /// Resumes the dequeue operation.
        /// </summary>
        public void Resume()
        {
            _pauseTcs.TrySetResult();
        }
    }
}