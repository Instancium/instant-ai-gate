using InstantAIGate.Infrastructure.Inference.layers;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Represents a queued inference request waiting for an available model context.
    /// </summary>
    public record PendingInferenceRequest(
        string RepoId,
        TaskCompletionSource<InferenceContext> CompletionSource,
        CancellationToken CancellationToken
    );
}