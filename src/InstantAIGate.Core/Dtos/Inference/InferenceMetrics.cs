namespace InstantAIGate.Core.Dtos.Inference
{

    /// <summary>
    /// Current throughput and queue metrics for telemetry.
    /// </summary>
    public record InferenceMetrics
    {
        /// <summary>
        /// Number of currently active context leases.
        /// </summary>
        public int ActiveLeases { get; init; }

        /// <summary>
        /// Number of pending requests in the queue.
        /// </summary>
        public int PendingRequests { get; init; }

        /// <summary>
        /// Initializes a new instance of the inference metrics.
        /// </summary>
        /// <param name="activeLeases">Number of active leases.</param>
        /// <param name="pendingRequests">Number of pending requests.</param>
        public InferenceMetrics(int activeLeases, int pendingRequests)
        {
            ActiveLeases = activeLeases;
            PendingRequests = pendingRequests;
        }
    }
}
