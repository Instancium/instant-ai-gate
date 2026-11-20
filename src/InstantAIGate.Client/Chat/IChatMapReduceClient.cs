namespace InstantAIGate.Client.Chat;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Capability contract for checkpointed Map-Reduce inference over a shared warm KV-cache prefix.
/// Encapsulates the full pipeline: prefix warm-up and pinning, per-chunk analysis with automatic
/// rollback, hierarchical summary compression, and final response generation with session-loss
/// recovery. Separated from <see cref="IChatClient"/> to keep linear chat contracts minimal.
/// </summary>
public interface IChatMapReduceClient
{
    /// <summary>
    /// Executes the pipeline and buffers the complete final response.
    /// </summary>
    /// <param name="request">The pipeline definition: warm-up prefix, chunk prompts, and final prompt factory.</param>
    /// <param name="cancellationToken">A token to cancel the pipeline.</param>
    /// <returns>The complete final response text.</returns>
    Task<string> CompleteAsync(ChatMapReduceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the pipeline and streams final response token deltas as they are generated.
    /// Map and reduce phases complete before the first token is yielded. Disposing the
    /// enumerator awaits completion of any in-flight final turn before releasing the session.
    /// </summary>
    /// <param name="request">The pipeline definition: warm-up prefix, chunk prompts, and final prompt factory.</param>
    /// <param name="cancellationToken">A token to cancel the pipeline.</param>
    /// <returns>An asynchronous stream of final response token deltas.</returns>
    IAsyncEnumerable<string> StreamAsync(ChatMapReduceRequest request, CancellationToken cancellationToken = default);
}
