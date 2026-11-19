namespace InstantAIGate.Client.Chat;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Capability contract for multimodal chat completion and token streaming against the gateway.
/// Separated from embeddings and telemetry to adhere to the Interface Segregation Principle.
/// </summary>
public interface IChatClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique gateway session identifier bound to this client instance.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Sends a structured <see cref="ChatMessage"/> and buffers the full generated response.
    /// </summary>
    /// <param name="message">The multimodal or text chat message to evaluate.</param>
    /// <param name="cancellationToken">A token to cancel the inference request.</param>
    /// <returns>The complete assistant response text.</returns>
    Task<string> CompleteAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a structured <see cref="ChatMessage"/> and invokes <paramref name="onToken"/>
    /// asynchronously as each generated token delta arrives from the gateway.
    /// </summary>
    /// <param name="message">The multimodal or text chat message to evaluate.</param>
    /// <param name="onToken">Callback invoked for each non-empty token delta.</param>
    /// <param name="cancellationToken">A token to cancel the streaming request.</param>
    Task StreamAsync(ChatMessage message, Func<string, Task> onToken, CancellationToken cancellationToken = default);
}
