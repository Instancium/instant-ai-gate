using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.Dtos.Inference
{
    /// <summary>
    /// Represents operational throughput metrics for the currently active model.
    /// </summary>
    public record InferenceMetrics(int ActiveLeases, int PendingRequestsCount);
}
